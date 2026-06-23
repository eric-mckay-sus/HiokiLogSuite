using InterProcessIO;

namespace HiokiNL2SQL.Tests.Services;

public class BlazorInputProviderTests
{
    [Fact]
    public async Task GetFilepathAsync_ShouldReturnPreviouslySetFileResult_WhenRequestArrivesLater()
    {
        var provider = new BlazorInputProvider();
        const string expectedPath = "C:/temp/uploads/test";

        provider.SetFileResult(expectedPath);

        var result = await provider.GetFilepathAsync(new Report("Please select the file(s) you wish to upload."));

        Assert.Equal(expectedPath, result);
    }
}
