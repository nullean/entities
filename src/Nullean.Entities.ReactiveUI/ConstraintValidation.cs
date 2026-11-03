using System.ComponentModel;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Collections;
using ReactiveUI.Validation.Components.Abstractions;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.States;

namespace Nullean.Entities.ReactiveUI;

/// <summary>How <see cref="ConstraintValidation.ValidateConstraints{TViewModel, T}"/> turns violations into validation components.</summary>
public sealed class ConstraintValidationOptions
{
	/// <summary>
	/// The lowest severity that makes the view model invalid. <see cref="Severity.Error"/> (the default) shows errors only; <see cref="Severity.Warning"/> adds warnings;
	/// <see cref="Severity.Hint"/> shows everything. A warning or hint that is shown makes its property invalid, because ReactiveUI.Validation has no other kind of message.
	/// </summary>
	public Severity MinimumSeverity { get; init; } = Severity.Error;

	/// <summary>
	/// Maps a <see cref="Violation.Member"/> of the domain type to the view model property it is shown on, for a domain member named differently from its view model property
	/// (<c>member =&gt; member == "Address" ? nameof(SignUpViewModel.Email) : member</c>). Return null to show that member's violations on the view model as a whole.
	/// Without it, the member name is used as is.
	/// </summary>
	public Func<string, string?>? PropertyFor { get; init; }

	/// <summary>The clock passed to <see cref="ConstraintSet{T}.Validate"/>, for rules such as <c>NotInFuture</c>. Defaults to <see cref="TimeProvider.System"/>.</summary>
	public TimeProvider? Time { get; init; }
}

/// <summary>Turns a <see cref="ConstraintSet{T}"/> into ReactiveUI.Validation components on a view model's <see cref="IValidationContext"/>.</summary>
public static class ConstraintValidation
{
	/// <summary>
	/// Validates <paramref name="constraints"/> against <c>project(viewModel)</c> now and again on every property change of <paramref name="viewModel"/>, and reports the
	/// violations on its <see cref="IValidatableViewModel.ValidationContext"/>: one property component per <see cref="Violation.Member"/> (so <c>BindValidation(ViewModel, vm =&gt; vm.Email, ...)</c>
	/// shows it), and one component for violations without a member, which shows in <see cref="IValidationComponent.Text"/>. One validation pass serves every component.
	/// Dispose the result to stop validating and remove the components.
	/// </summary>
	/// <param name="viewModel">The view model; its <c>PropertyChanged</c> event triggers validation.</param>
	/// <param name="constraints">The rules, usually the domain type's <c>Constraints</c> or a companion's <c>All</c>.</param>
	/// <param name="project">Builds the domain value from the view model's current state. Called on every property change, on the thread that raised it.</param>
	/// <param name="options">Severity, member mapping and clock; see <see cref="ConstraintValidationOptions"/>.</param>
	public static IDisposable ValidateConstraints<TViewModel, T>(
		this TViewModel viewModel,
		ConstraintSet<T> constraints,
		Func<TViewModel, T> project,
		ConstraintValidationOptions? options = null
	) where TViewModel : class, INotifyPropertyChanged, IValidatableViewModel
	{
		ArgumentNullException.ThrowIfNull(viewModel);
		ArgumentNullException.ThrowIfNull(constraints);
		ArgumentNullException.ThrowIfNull(project);
		return new ConstraintValidator<TViewModel, T>(viewModel, constraints, project, options ?? new ConstraintValidationOptions());
	}
}

internal sealed class ConstraintValidator<TViewModel, T> : IDisposable where TViewModel : class, INotifyPropertyChanged, IValidatableViewModel
{
	private readonly TViewModel _viewModel;
	private readonly ConstraintSet<T> _constraints;
	private readonly Func<TViewModel, T> _project;
	private readonly ConstraintValidationOptions _options;
	private readonly IValidationContext _context;
	private readonly ConstraintComponent _entity = new();
	private readonly Dictionary<string, PropertyComponent> _properties = [];
	private readonly Lock _gate = new();
	private bool _disposed;
	private bool _validating;
	private bool _again;

	public ConstraintValidator(TViewModel viewModel, ConstraintSet<T> constraints, Func<TViewModel, T> project, ConstraintValidationOptions options)
	{
		_viewModel = viewModel;
		_constraints = constraints;
		_project = project;
		_options = options;
		_context = viewModel.ValidationContext;
		_context.Add(_entity);
		Validate();
		_viewModel.PropertyChanged += OnPropertyChanged;
	}

	private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Validate();

	private void Validate()
	{
		lock (_gate)
		{
			// A component update can make the view model change a property of its own (a CanSubmit flag bound to the context's validity), which raises
			// PropertyChanged and calls back in here on the same thread. Run that pass after this one instead of inside it.
			if (_validating)
			{
				_again = true;
				return;
			}

			_validating = true;
			try
			{
				do
				{
					_again = false;
					if (_disposed)
					{
						return;
					}
					Pass();
				}
				while (_again);
			}
			finally
			{
				_validating = false;
			}
		}
	}

	private void Pass()
	{
		var entity = new List<string>();
		var byProperty = new Dictionary<string, List<string>>();
		foreach (var v in _constraints.Validate(_project(_viewModel), _options.Time))
		{
			if (v.Severity > _options.MinimumSeverity)
			{
				continue;
			}

			var property = v.Member is null ? null : _options.PropertyFor is { } map ? map(v.Member) : v.Member;
			if (property is null)
			{
				entity.Add(v.Message);
				continue;
			}

			if (!byProperty.TryGetValue(property, out var messages))
			{
				byProperty[property] = messages = [];
			}
			messages.Add(v.Message);
		}

		List<PropertyComponent> added = [];
		foreach (var (name, messages) in byProperty)
		{
			if (!_properties.ContainsKey(name))
			{
				// A component per property is created the first time that property has a violation, already invalid, and kept from then on.
				var component = new PropertyComponent(name);
				component.Update(messages);
				_properties[name] = component;
				added.Add(component);
			}
		}

		_entity.Update(entity);
		foreach (var component in _properties.Values.ToArray())
		{
			component.Update(byProperty.TryGetValue(component.Property, out var messages) ? messages : []);
		}
		foreach (var component in added)
		{
			_context.Add(component);
		}
	}

	public void Dispose()
	{
		List<IValidationComponent> components;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			_viewModel.PropertyChanged -= OnPropertyChanged;
			components = [_entity, .. _properties.Values];
		}

		if (!_context.IsDisposed)
		{
			_context.RemoveMany(components);
		}
		foreach (var component in components)
		{
			((ConstraintComponent)component).Complete();
		}
	}
}

/// <summary>A validation component whose state is pushed in by <see cref="ConstraintValidator{TViewModel, T}"/>; replays its current state to every new subscriber.</summary>
internal class ConstraintComponent : IValidationComponent
{
	private readonly Lock _gate = new();
	private readonly List<IObserver<IValidationState>> _observers = [];
	private IValidationState _state = ValidationState.Valid;
	private string[] _messages = [];
	private bool _completed;

	public IValidationText? Text => _state.Text;

	public bool IsValid => _state.IsValid;

	public IObservable<IValidationState> ValidationStatusChange => new StatusChanges(this);

	/// <summary>Sets the messages; publishes only when they changed, so a view model that reacts to validity does not loop.</summary>
	public void Update(IReadOnlyList<string> messages)
	{
		IObserver<IValidationState>[] observers;
		IValidationState state;
		lock (_gate)
		{
			if (_completed || messages.SequenceEqual(_messages, StringComparer.Ordinal))
			{
				return;
			}
			_messages = [.. messages];
			_state = state = _messages.Length == 0 ? ValidationState.Valid : new ValidationState(false, ValidationText.Create(_messages));
			observers = [.. _observers];
		}
		foreach (var observer in observers)
		{
			observer.OnNext(state);
		}
	}

	public void Complete()
	{
		IObserver<IValidationState>[] observers;
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}
			_completed = true;
			observers = [.. _observers];
			_observers.Clear();
		}
		foreach (var observer in observers)
		{
			observer.OnCompleted();
		}
	}

	private IDisposable Subscribe(IObserver<IValidationState> observer)
	{
		IValidationState state;
		bool completed;
		lock (_gate)
		{
			state = _state;
			completed = _completed;
			if (!completed)
			{
				_observers.Add(observer);
			}
		}
		observer.OnNext(state);
		if (completed)
		{
			observer.OnCompleted();
			return NoSubscription.Instance;
		}
		return new Subscription(this, observer);
	}

	private void Unsubscribe(IObserver<IValidationState> observer)
	{
		lock (_gate)
		{
			_ = _observers.Remove(observer);
		}
	}

	private sealed class StatusChanges(ConstraintComponent owner) : IObservable<IValidationState>
	{
		public IDisposable Subscribe(IObserver<IValidationState> observer) => owner.Subscribe(observer);
	}

	private sealed class Subscription(ConstraintComponent owner, IObserver<IValidationState> observer) : IDisposable
	{
		public void Dispose() => owner.Unsubscribe(observer);
	}

	private sealed class NoSubscription : IDisposable
	{
		public static readonly NoSubscription Instance = new();

		public void Dispose() { }
	}
}

/// <summary>A component for one view model property, found by <c>BindValidation</c> and <c>ObserveFor</c> through <see cref="IValidatesProperties"/>.</summary>
internal sealed class PropertyComponent(string property) : ConstraintComponent, IPropertyValidationComponent
{
	public string Property => property;

	public int PropertyCount => 1;

	public IEnumerable<string> Properties => [property];

	public bool ContainsPropertyName(string propertyName, bool exclusively = false) => string.Equals(propertyName, property, StringComparison.Ordinal);
}
