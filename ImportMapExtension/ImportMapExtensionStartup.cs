using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Toolbelt.Blazor.WebAssembly.ExtensibleGateway.ImportMapExtension;

[assembly: HostingStartup(typeof(ImportMapExtensionStartup))]

namespace Toolbelt.Blazor.WebAssembly.ExtensibleGateway.ImportMapExtension;

public class ImportMapExtensionStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, ImportMapExtensionStartupFilter>();
        });
    }
}
