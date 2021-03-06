using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SuperSocket.ProtoBase;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using SuperSocket.Server;

namespace SuperSocket.Tests
{

    public class AspNetIntegrationTest : TestClassBase
    {
        public interface ITestService
        {
            void Test();
        }

        public class TestService : ITestService
        {
            public void Test()
            {

            }
        }

        public class Startup
        {
            public Startup(IConfiguration configuration)
            {
                Configuration = configuration;
            }

            public IConfiguration Configuration { get; }

            // This method gets called by the runtime. Use this method to add services to the container.
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddSingleton<ITestService, TestService>();
            }

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IHostEnvironment env)
            {
                app.UseRouting();
            }
        }

        public AspNetIntegrationTest(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
            
        }

        private const int _defaultPort = 5050;


        [Fact]
        public async ValueTask TestSingleHostServiceAccess()
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(config =>
                    {
                        config.Listen(IPAddress.Any, _defaultPort);
                    });
                })
                .AsSuperSocketHostBuilder<TextPackageInfo, LinePipelineFilter>();

            using (var host = builder.Build())
            { 
                await host.StartAsync();
                var server = host.AsServer();

                Assert.IsType<TestService>(server.ServiceProvider.GetService<ITestService>());
                
                await host.StopAsync();
            }
        }


        [Fact]
        public async ValueTask TestMultipleHostServiceAccess()
        {
            var builder = Host.CreateDefaultBuilder()
                 .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(config =>
                    {
                        config.Listen(IPAddress.Any, _defaultPort);
                    });
                })
                .AsMultipleServerHostBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.Sources.Clear();
                    config.AddJsonFile("Config/multiple_server.json", optional: false, reloadOnChange: true);
                })
                .AddServer<TextPackageInfo, LinePipelineFilter>(builder =>
                {
                    builder
                        .ConfigureServerOptions((ctx, config) =>
                        {
                            return config.GetSection("TestServer1");
                        });
                });

            using (var host = builder.Build())
            { 
                await host.StartAsync();
                var server = host.AsServer();

                Assert.IsType<RegularHostConfigurator>(server.ServiceProvider.GetService<IHostConfigurator>());
                
                await host.StopAsync();
            }
        }

        public class TestSession : AppSession
        {
            public ITestService TestService { get; private set; }

            public TestSession(IServiceProvider serviceProvider)
            {
                TestService = serviceProvider.GetRequiredService<ITestService>();
            }
        }

        [Fact]
        public async ValueTask TestMultipleHostServiceAccess2()
        {
            TestSession session = default;

            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(config =>
                    {
                        config.Listen(IPAddress.Any, _defaultPort);
                    });
                })
                .AsMultipleServerHostBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.Sources.Clear();
                    config.AddJsonFile("Config/multiple_server.json", optional: false, reloadOnChange: true);
                })
                .AddServer<TextPackageInfo, LinePipelineFilter>(builder =>
                {
                    builder
                        .ConfigureServerOptions((ctx, config) =>
                        {
                            return config.GetSection("TestServer1");
                        })
                        .UseSession<TestSession>()
                        .UseSessionHandler(s =>
                        {
                            session = s as TestSession;
                            return new ValueTask();
                        });
                });

            using (var host = builder.Build())
            { 
                await host.StartAsync();
                var server = host.AsServer();
                
                using(var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    await client.ConnectAsync(GetDefaultServerEndPoint());
                    OutputHelper.WriteLine("Connected.");
                    await Task.Delay(1000);

                    Assert.NotNull(session);
                    Assert.NotNull(session.TestService as TestService);

                    client.Shutdown(SocketShutdown.Both);
                    client.Close();
                }                

                await host.StopAsync();
            }
        }
    }
}