using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Pins that <see cref="DiagnosticsEndpoint.BuildResponse"/> redacts every
/// event independently of <see cref="DiagnosticLogWriter"/>'s own
/// format-time redaction - see the endpoint's own remarks on why the ring
/// buffer's raw events need a second redaction pass here.
/// </summary>
public class DiagnosticsEndpointTests
{
    private static readonly PathRedactor Redactor = new(new[] { (@"C:\Users\RealPlayer", "%USERPROFILE%") });

    [Fact]
    public void BuildResponse_RedactsMessage()
    {
        var events = new[]
        {
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Info, "Discovery", @"Bindings folder: C:\Users\RealPlayer\AppData\Local\... -> found"),
        };

        var response = DiagnosticsEndpoint.BuildResponse(events, Redactor);

        Assert.DoesNotContain("RealPlayer", response[0].Message);
        Assert.Contains("%USERPROFILE%", response[0].Message);
    }

    [Fact]
    public void BuildResponse_RedactsDetail_WhenPresent()
    {
        var events = new[]
        {
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Warn, "Discovery", "message", @"C:\Users\RealPlayer\secret"),
        };

        var response = DiagnosticsEndpoint.BuildResponse(events, Redactor);

        Assert.NotNull(response[0].Detail);
        Assert.DoesNotContain("RealPlayer", response[0].Detail!);
    }

    [Fact]
    public void BuildResponse_DetailNull_StaysNull()
    {
        var events = new[]
        {
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Info, "Discovery", "message"),
        };

        var response = DiagnosticsEndpoint.BuildResponse(events, Redactor);

        Assert.Null(response[0].Detail);
    }

    [Fact]
    public void BuildResponse_PreservesOrder_OldestFirst()
    {
        var events = new[]
        {
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Info, "A", "first"),
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Info, "B", "second"),
        };

        var response = DiagnosticsEndpoint.BuildResponse(events, Redactor);

        Assert.Equal("first", response[0].Message);
        Assert.Equal("second", response[1].Message);
    }

    [Fact]
    public void BuildResponse_MapsLevelAndCategory()
    {
        var events = new[]
        {
            new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Error, "Injection", "boom"),
        };

        var response = DiagnosticsEndpoint.BuildResponse(events, Redactor);

        Assert.Equal("Error", response[0].Level);
        Assert.Equal("Injection", response[0].Category);
    }
}
