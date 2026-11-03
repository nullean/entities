using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nullean.Entities.ComponentModel;

/// <summary>
/// A minimal view-model base for an app without an MVVM toolkit: <see cref="INotifyPropertyChanged"/> plus <see cref="INotifyDataErrorInfo"/> over a
/// <see cref="ConstraintErrors{T}"/>, revalidating after every <see cref="SetProperty{TField}"/> that changes a value. With a toolkit base class
/// (CommunityToolkit.Mvvm's <c>ObservableObject</c>, ReactiveUI's <c>ReactiveObject</c>), own a <see cref="ConstraintErrors{T}"/> instead.
/// </summary>
/// <typeparam name="T">The domain value the constraint set is over.</typeparam>
public abstract class ConstraintViewModel<T> : INotifyPropertyChanged, INotifyDataErrorInfo
{
	/// <param name="constraints">The rules over the domain value <see cref="ToValue"/> builds.</param>
	/// <param name="options">Severity and member mapping; see <see cref="ConstraintErrorsOptions"/>.</param>
	protected ConstraintViewModel(ConstraintSet<T> constraints, ConstraintErrorsOptions? options = null) =>
		Errors = new ConstraintErrors<T>(constraints, ToValue, this, options);

	/// <summary>The helper this view model delegates to: its violations, and <see cref="ConstraintErrors{T}.Validate(T)"/> for an edit.</summary>
	protected ConstraintErrors<T> Errors { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged { add => Errors.ErrorsChanged += value; remove => Errors.ErrorsChanged -= value; }

	public bool HasErrors => Errors.HasErrors;

	public IEnumerable GetErrors(string? propertyName) => Errors.GetErrors(propertyName);

	/// <summary>Builds the domain value from the current property values. Called on every validation, so keep it cheap and side-effect free.</summary>
	protected abstract T ToValue();

	/// <summary>
	/// Runs the constraints. Validation is not run on construction (an empty form shows no errors until it is edited); call this to show them up front, for
	/// example before a save.
	/// </summary>
	public void Validate() => Revalidate();

	/// <summary>
	/// What a property change runs: <c>Errors.Validate()</c>. Override to call <c>Errors.Validate(original)</c> when editing an existing entity, so the rules
	/// about a change run too.
	/// </summary>
	protected virtual IReadOnlyList<Violation> RunConstraints() => Errors.Validate();

	/// <summary>Sets <paramref name="field"/>, raises <see cref="PropertyChanged"/> and revalidates; returns false and does nothing when the value is equal.</summary>
	protected bool SetProperty<TField>(ref TField field, TField value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<TField>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		OnPropertyChanged(propertyName);
		Revalidate();
		return true;
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	private void Revalidate()
	{
		var had = HasErrors;
		_ = RunConstraints();
		if (had != HasErrors)
		{
			OnPropertyChanged(nameof(HasErrors));
		}
	}
}
