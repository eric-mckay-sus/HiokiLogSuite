using System.Reflection;
using HiokiNL2SQLMark1.Components.Pages;
using InterProcessIO;

namespace HiokiNL2SQL.Tests.Services;

public class UploadLogsTests
{
    [Fact]
    public async Task OnInitializedAsync_ShouldSubscribeToReporterNotifications()
    {
        var component = new TestableUploadLogs
        {
            InputProvider = new BlazorInputProvider(),
            Reporter = new BlazorReporter()
        };

        await component.Initialize();

        FieldInfo? onNotifyField = typeof(BlazorReporter).GetField("OnNotify", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onNotifyField);

        var handler = onNotifyField!.GetValue(component.Reporter) as MulticastDelegate;
        Assert.NotNull(handler);
        Assert.Contains(handler!.GetInvocationList(), d => d.Target == component && d.Method.Name == "StateHasChanged");
    }

    private sealed class TestableUploadLogs : UploadLogs
    {
        public Task Initialize() => this.OnInitializedAsync();
    }
}
