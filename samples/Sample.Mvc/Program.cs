using Romatech.Extensions.Ai.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AI enablement — zero boilerplate
builder.Services.UseMcp();
builder.Services.UseRag(options =>
{
    options.IncludeXmlDocs = true;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMcp();
app.UseRag();

app.UseAuthorization();
app.MapControllers();

app.Run();
