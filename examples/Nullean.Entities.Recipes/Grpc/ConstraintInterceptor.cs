// gRPC (Grpc.AspNetCore): a server interceptor that validates each unary request message and answers INVALID_ARGUMENT, with the violations as a
// google.rpc.BadRequest in the status details (Grpc.StatusProto), so a client reads them with RpcException.GetRpcStatus()?.GetDetail<BadRequest>().

using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.Grpc;

public sealed class ConstraintInterceptor(IConstraintRegistry registry) : Interceptor
{
	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation
	)
	{
		if (!registry.TryValidate(request, out var violations) || !violations.Any(v => v.Severity == Severity.Error))
		{
			return continuation(request, context);
		}
		var badRequest = new BadRequest();
		foreach (var v in violations.Where(v => v.Severity == Severity.Error))
		{
			badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation { Field = v.Member ?? "", Description = v.Message });
		}
		throw new Google.Rpc.Status
		{
			Code = (int)Code.InvalidArgument,
			Message = "Request violates its constraints",
			Details = { Any.Pack(badRequest) },
		}.ToRpcException();
	}
}

// The request is a generated protobuf class, so its rules live in a companion.
[Constraints<SignUpRequest>]
public static partial class SignUpRequestRules
{
	public static readonly Constraint<SignUpRequest> HasEmail = Constraints<SignUpRequest>.Check(
		r => r.Email.Contains('@') ? [] : [Violation.Error(nameof(SignUpRequest.Email), "must be an email address")]
	);
}

public sealed class SignUpService : SignUps.SignUpsBase
{
	public override Task<SignUpReply> Register(SignUpRequest request, ServerCallContext context) =>
		Task.FromResult(new SignUpReply { Id = $"user:{request.Email}" });
}
