using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class ApiRouteRegistrationTests
{
    [Fact]
    public void EveryControllerAction_IsRegisteredAsAnApiEndpoint()
    {
        using var factory = new IsolatedApiFactory();
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        var controllerEndpoints = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToArray();

        Assert.NotEmpty(controllerEndpoints);
        Assert.Equal(
            controllerEndpoints.Length,
            controllerEndpoints
                .Select(endpoint =>
                {
                    var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
                    var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                    return $"{string.Join(',', methods)} {endpoint.RoutePattern.RawText} => {action.ControllerName}.{action.ActionName}";
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.True(
            controllerEndpoints.Length >= 70,
            $"Expected all website API actions to be registered, but found only {controllerEndpoints.Length}.");
    }
}
