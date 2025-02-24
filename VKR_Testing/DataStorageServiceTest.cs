using System.Threading.Tasks;
using Xunit;
using VKR_Core;
using VKR_Core.Services;
using Assert = Xunit.Assert;

public class DataStorageServiceTests
{
    private readonly DataStorageService _dataStorage;

    public DataStorageServiceTests()
    {
        _dataStorage = new DataStorageService();
    }

    [Fact]
    public async Task SaveFile_ShouldStoreDataSuccessfully()
    {
        var data = new byte[] { 1, 2, 3 };
        await _dataStorage.SaveFileAsync("file1", data);

        var loadedData = await _dataStorage.LoadFileAsync("file1");
        Assert.Equal(data, loadedData);
    }

    [Fact]
    public async Task DeleteFile_ShouldRemoveFileSuccessfully()
    {
        var data = new byte[] { 1, 2, 3 };
        await _dataStorage.SaveFileAsync("file1", data);
        await _dataStorage.DeleteFileAsync("file1");

        var loadedData = await _dataStorage.LoadFileAsync("file1");
        Assert.Null(loadedData);
    }
}