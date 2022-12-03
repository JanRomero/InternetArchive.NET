﻿global using InternetArchive;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Threading.Tasks;
global using static InternetArchiveTests.Init;
global using Microsoft.AspNetCore.JsonPatch;
global using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace InternetArchiveTests;

[TestClass()]
public class Init
{
    internal static Client _client = null!;
    internal static Config _config = null!;

    internal static DateOnly _startDateOnly, _endDateOnly;
    internal static DateTime _startDateTime, _endDateTime;

    [AssemblyInitialize]
    public static async Task TestInitialize(TestContext _)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.private.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        _config = configurationBuilder.Get<Config>() ?? throw new Exception("_config is null");

        if (string.IsNullOrEmpty(_config.AccessKey))
        {
            throw new Exception("To run tests, please create a private settings file or set environment variables. For details visit https://github.com/experimentaltvcenter/InternetArchive.NET/blob/main/docs/DEVELOPERS.md#unit-tests");
        }

        ServiceExtensions.Services.AddLogging(configure => configure.AddConsole(options => options.FormatterName = ConsoleFormatterNames.Systemd));

        _client = Client.Create(_config.AccessKey, _config.SecretKey);
        _client.RequestInteractivePriority();

        _endDateTime = DateTime.Today.AddDays(-1);
        _startDateTime = _endDateTime.AddDays(-7);

        _endDateOnly = new DateOnly(_endDateTime.Year, _endDateTime.Month, _endDateTime.Day);
        _startDateOnly = new DateOnly(_startDateTime.Year, _startDateTime.Month, _startDateTime.Day);

        if (!File.Exists(_config.LocalFilename)) File.WriteAllText(_config.LocalFilename, "test file for unit tests - ok to delete");

        using var response = await _client.Metadata.ReadAsync(_config.TestItem);
        if (response.IsDark == true)
        {
            await _client.Tasks.MakeUndarkAsync(_config.TestItem, "used in automated tests");
            await WaitForServerAsync(_config.TestItem);
        }

        if (!response.Files.Any(x => x.Format == "Text" && x.Name == _config.RemoteFilename))
        {
            await CreateTestItemAsync(_config.TestItem);
        }
    }

    public static async Task<string> CreateTestItemAsync(string? identifier = null, IEnumerable<KeyValuePair<string, object?>>? extraMetadata = null)
    {
        identifier ??= $"etc-tmp-{Guid.NewGuid():N}";

        var metadata = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("collection", "test_collection"),
            new KeyValuePair<string, object?>("mediatype", "texts"),
            new KeyValuePair<string, object?>("noindex", "true"),
        };

        if (extraMetadata != null) metadata.AddRange(extraMetadata);

        await _client.Item.PutAsync(new Item.PutRequest
        {
            Bucket = identifier,
            LocalPath = _config.LocalFilename,
            RemoteFilename = _config.RemoteFilename,
            Metadata = metadata,
            CreateBucket = true,
            NoDerive = true
        });

        await WaitForServerAsync(identifier);
        return identifier;
    }

    public static async Task WaitForServerAsync(string identifier, int retries = 200, int secondsBetween = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            var taskRequest = new Tasks.GetRequest { Identifier = identifier };
            var response = await _client.Tasks.GetAsync(taskRequest);
            Assert.IsTrue(response.Success);

            var summary = response.Value!.Summary!;
            Assert.IsTrue(summary.Error == 0);

            if (summary.Queued == 0 && summary.Running == 0) return;
            await Task.Delay(secondsBetween * 1000);
        }

        Assert.Fail("timeout exceeded");
    }
}
