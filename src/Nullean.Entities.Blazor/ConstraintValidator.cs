using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Nullean.Entities.Blazor;

/// <summary>
/// Validates an <c>EditForm</c>'s model against a <see cref="ConstraintSet{T}"/> and writes the violations into the form's validation state, so
/// <c>&lt;ValidationMessage For="..."&gt;</c> and <c>&lt;ValidationSummary&gt;</c> show them. Put it inside the form, next to (or instead of) <c>DataAnnotationsValidator</c>:
/// <code>&lt;ConstraintValidator Constraints="SignUp.Constraints" /&gt;</code>
/// On submit (<see cref="EditContext.Validate"/>) the whole model is checked. When a field changes, the set runs again and that field gets its current messages; on the other
/// fields, a message that no longer applies is removed, but no new one is added to a field the user has not touched yet.
/// A violation is keyed by <see cref="Violation.Member"/> on the form model (<c>new FieldIdentifier(model, member)</c>); one without a member is a model-level message,
/// which <c>ValidationSummary</c> shows.
/// </summary>
/// <typeparam name="TModel">The form model's type. Razor infers it from <see cref="Constraints"/>.</typeparam>
public sealed class ConstraintValidator<TModel> : ComponentBase, IDisposable where TModel : class
{
	private EditContext? _editContext;
	private ValidationMessageStore? _store;
	// What this validator currently shows, per field. ValidationMessageStore cannot be enumerated, and a field change needs to know what is shown on the other fields.
	private Dictionary<FieldIdentifier, List<string>> _shown = [];

	[CascadingParameter]
	private EditContext? CurrentEditContext { get; set; }

	/// <summary>The rules for the form model: <c>T.Constraints</c> for a type that declares its own, or a companion's <c>All</c>.</summary>
	[Parameter, EditorRequired]
	public ConstraintSet<TModel> Constraints { get; set; } = null!;

	/// <summary>
	/// The least severe violation to show. <see cref="Severity.Error"/> (the default) shows errors only; <see cref="Severity.Warning"/> adds warnings and
	/// <see cref="Severity.Hint"/> shows everything. Every message shown counts as invalid to the form, so a shown warning also blocks <c>OnValidSubmit</c>.
	/// </summary>
	[Parameter]
	public Severity MinimumSeverity { get; set; } = Severity.Error;

	/// <summary>The clock for rules that read the time (<c>NotInFuture</c>, <c>Always((x, now) =&gt; ...)</c>). Defaults to <see cref="TimeProvider.System"/>.</summary>
	[Parameter]
	public TimeProvider? Time { get; set; }

	protected override void OnInitialized()
	{
		if (CurrentEditContext is null)
		{
			throw new InvalidOperationException(
				$"{nameof(ConstraintValidator<>)} requires a cascading parameter of type {nameof(EditContext)}. Put it inside an EditForm."
			);
		}
		_editContext = CurrentEditContext;
		_store = new ValidationMessageStore(_editContext);
		_editContext.OnValidationRequested += ValidationRequested;
		_editContext.OnFieldChanged += FieldChanged;
	}

	protected override void OnParametersSet()
	{
		if (Constraints is null)
		{
			throw new InvalidOperationException($"{nameof(ConstraintValidator<>)} requires a {nameof(Constraints)} parameter.");
		}
		if (CurrentEditContext != _editContext)
		{
			throw new InvalidOperationException($"{nameof(ConstraintValidator<>)} does not support changing the {nameof(EditContext)} dynamically.");
		}
	}

	private void ValidationRequested(object? sender, ValidationRequestedEventArgs e)
	{
		_shown = Group(Validate());
		Publish();
	}

	private void FieldChanged(object? sender, FieldChangedEventArgs e)
	{
		var current = Group(Validate());
		var next = new Dictionary<FieldIdentifier, List<string>>();
		// Other fields keep only the messages that still apply: fixing Password clears "must match Password" on Confirm, but typing in Email does not flag an untouched Name.
		foreach (var (field, messages) in _shown)
		{
			if (field.Equals(e.FieldIdentifier))
			{
				continue;
			}
			var still = current.TryGetValue(field, out var now) ? [.. messages.Where(now.Contains)] : new List<string>();
			if (still.Count > 0)
			{
				next[field] = still;
			}
		}
		if (current.TryGetValue(e.FieldIdentifier, out var changed))
		{
			next[e.FieldIdentifier] = changed;
		}
		_shown = next;
		Publish();
	}

	private IReadOnlyList<Violation> Validate()
	{
		var model = _editContext!.Model;
		if (model is not TModel typed)
		{
			throw new InvalidOperationException(
				$"{nameof(ConstraintValidator<>)}: the form model is a {model.GetType().Name}, but the constraints are for {typeof(TModel).Name}."
			);
		}
		return Constraints.Validate(typed, Time);
	}

	private Dictionary<FieldIdentifier, List<string>> Group(IReadOnlyList<Violation> violations)
	{
		var model = _editContext!.Model;
		var byField = new Dictionary<FieldIdentifier, List<string>>();
		foreach (var v in violations)
		{
			if (v.Severity > MinimumSeverity)
			{
				continue;
			}
			var field = new FieldIdentifier(model, v.Member ?? string.Empty);
			if (!byField.TryGetValue(field, out var messages))
			{
				byField[field] = messages = [];
			}
			messages.Add(v.Message);
		}
		return byField;
	}

	private void Publish()
	{
		_store!.Clear();
		foreach (var (field, messages) in _shown)
		{
			_store.Add(field, messages);
		}
		_editContext!.NotifyValidationStateChanged();
	}

	public void Dispose()
	{
		if (_editContext is not null)
		{
			_editContext.OnValidationRequested -= ValidationRequested;
			_editContext.OnFieldChanged -= FieldChanged;
			_store?.Clear();
			_editContext.NotifyValidationStateChanged();
		}
	}
}
