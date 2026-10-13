using OCS.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOcsApi(builder.Configuration);

var app = builder.Build();

app.UseOcsApi();

app.Run();

public partial class Program;
