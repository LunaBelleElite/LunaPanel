using LunaPanel.Server.Hosting;

var options = RealServerEnvironment.Build();
var app = ServerHostBuilder.Build(args, options);
app.Run();
