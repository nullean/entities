---
navigation_title: gRPC
description: Validate gRPC request messages in a server interceptor and answer INVALID_ARGUMENT with the violations as google.rpc.BadRequest.
---

# gRPC services (Grpc.AspNetCore)

No package: this is a recipe. It is compiled in
[`examples/Nullean.Entities.Recipes/Grpc`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Recipes/Grpc) and tested in
`tests/Nullean.Entities.Recipes.Tests/GrpcInterceptorTests.cs`, which makes real gRPC calls through ASP.NET Core's `TestServer` with `Grpc.Net.Client`.

A request message is a class generated from your `.proto`, so its rules go in a companion:

```csharp
[Constraints<SignUpRequest>]
public static partial class SignUpRequestRules
{
    public static readonly Constraint<SignUpRequest> HasEmail = Constraints<SignUpRequest>.Check(
        r => r.Email.Contains('@') ? [] : [Violation.Error(nameof(SignUpRequest.Email), "must be an email address")]
    );
}
```

A server interceptor validates every unary request whose type is registered and answers `INVALID_ARGUMENT`. With `Grpc.StatusProto` the violations travel as the standard
[`google.rpc.BadRequest`](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto) detail, one `FieldViolation` per violation:

```csharp
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

services.AddConstraints(SignUpRequestRules.All);
services.AddGrpc(o => o.Interceptors.Add<ConstraintInterceptor>());
```

A .NET client reads the violations back with `Grpc.StatusProto` too:

```csharp
catch (RpcException e) when (e.StatusCode == StatusCode.InvalidArgument)
{
    var badRequest = e.GetRpcStatus()?.GetDetail<BadRequest>();   // badRequest.FieldViolations[0].Field == "Email"
}
```

`Violation.Member` uses the C# property name (`Email`), not the proto field name (`email`); map it if your clients expect the latter. Streaming calls need the same check in
`ClientStreamingServerHandler`/`DuplexStreamingServerHandler` per message read, which this recipe leaves out.

Tested with Grpc.AspNetCore, Grpc.StatusProto and Grpc.Net.Client 2.85.0.
