﻿using System.Collections.Concurrent;

using StreamMaster.Application.Services;
using StreamMaster.Domain.Crypto;

namespace StreamMaster.Application.StreamGroups.Commands;

[SMAPI]
[TsInterface(AutoI = false, IncludeNamespace = false, FlattenHierarchy = true, AutoExportMethods = false)]
public record CreateStreamGroupRequest(string Name, string? OutputProfileName, string? CommandProfileName, string? GroupKey, bool? CreateSTRM) : IRequest<APIResponse>;

[LogExecutionTimeAspect]
public class CreateStreamGroupRequestHandler(IRepositoryWrapper Repository, IMessageService messageService, IOptionsMonitor<Setting> intSettings, IDataRefreshService dataRefreshService, IBackgroundTaskQueue taskQueue)
    : IRequestHandler<CreateStreamGroupRequest, APIResponse>
{
    public async Task<APIResponse> Handle(CreateStreamGroupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            return APIResponse.NotFound;
        }

        if (request.Name.EqualsIgnoreCase("all"))
        {
            return APIResponse.ErrorWithMessage($"The name '{request.Name}' is reserved");
        }

        ConcurrentDictionary<string, byte> generatedIdsDict = new();
        foreach (StreamGroup sg in Repository.StreamGroup.GetQuery())
        {
            _ = generatedIdsDict.TryAdd(sg.DeviceID, 0);
        }

        StreamGroup streamGroup = new()
        {
            Name = request.Name,
            DeviceID = UniqueHexGenerator.GenerateUniqueHex(generatedIdsDict),
            GroupKey = request.GroupKey ?? KeyGenerator.GenerateKey(),
            CreateSTRM = request.CreateSTRM ?? false,
        };

        streamGroup.StreamGroupProfiles.Add(new StreamGroupProfile
        {
            ProfileName = "Default",
            OutputProfileName = request.OutputProfileName ?? intSettings.CurrentValue.DefaultOutputProfileName,
            CommandProfileName = request.CommandProfileName ?? intSettings.CurrentValue.DefaultCommandProfileName
        });

        Repository.StreamGroup.CreateStreamGroup(streamGroup);
        _ = await Repository.SaveAsync();

        await dataRefreshService.RefreshStreamGroups();

        await messageService.SendSuccess("Stream Group '" + request.Name + "' added successfully");

        if (streamGroup.CreateSTRM)
        {
            await taskQueue.CreateSTRMFiles(cancellationToken);
        }

        return APIResponse.Ok;
    }
}