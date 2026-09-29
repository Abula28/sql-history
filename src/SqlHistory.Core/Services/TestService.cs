using Microsoft.Extensions.Logging;

namespace SqlHistory.Core.Services;

public class TestService
{
    private readonly ILogger<TestService> _logger;

    public TestService(ILogger<TestService> logger)
    {
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("Hello World!");
        Menu();
        while (true)
        {
        }
    }



    private void Menu()
    {
        Console.WriteLine("");
        Console.WriteLine("-- ====== MENU ====== --");
        Console.WriteLine("");
        
    }
}