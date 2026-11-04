// martinothamar/Mediator 3 (source-generated): a pipeline behaviour constrained to messages that declare their own constraints, so it reads T.Constraints directly.
// The generator closes it only over message types that satisfy the constraint; no registry and no reflection.

using Mediator;

namespace Nullean.Entities.Recipes.Mediator;

public sealed class ConstraintBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage, IHaveConstraints<TMessage>
{
	public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
	{
		var violations = TMessage.Constraints.Validate(message);
		if (violations.Any(v => v.Severity == Severity.Error))
		{
			throw new ConstraintViolationException(violations);
		}
		return next(message, cancellationToken);
	}
}

// IHaveConstraints<PlaceOrder> is spelled out although the [Constraint] generator adds it too: source generators do not see each other's output,
// so without it Mediator's generator finds no message that satisfies the behaviour's constraint and silently leaves the behaviour out.
public sealed partial record PlaceOrder(string Sku, int Quantity) : ICommand<string>, IHaveConstraints<PlaceOrder>
{
	[Constraint]
	public static readonly Constraint<PlaceOrder> PositiveQuantity = Constraints<PlaceOrder>.Check(
		o => o.Quantity > 0 ? [] : [Violation.Error(nameof(Quantity), "must be at least 1")]
	);
}

public sealed class PlaceOrderHandler : ICommandHandler<PlaceOrder, string>
{
	public ValueTask<string> Handle(PlaceOrder command, CancellationToken cancellationToken) =>
		ValueTask.FromResult($"placed {command.Quantity} x {command.Sku}");
}
