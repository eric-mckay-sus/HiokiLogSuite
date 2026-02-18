namespace HiokiNL2SQLMark1.Logic;

public interface IJSService
{
    Task FocusElement(string elementId);
    Task DownloadCsv(string fileName, string csvContent);
}