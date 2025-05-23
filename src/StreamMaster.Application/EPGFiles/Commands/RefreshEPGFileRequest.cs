﻿namespace StreamMaster.Application.EPGFiles.Commands;

[SMAPI]
[TsInterface(AutoI = false, IncludeNamespace = false, FlattenHierarchy = true, AutoExportMethods = false)]
public record RefreshEPGFileRequest(int Id) : IRequest<APIResponse>;

public class RefreshEPGFileRequestHandler(ILogger<RefreshEPGFileRequest> Logger, IFileUtilService fileUtilService, IMessageService messageService, IMapper Mapper, IJobStatusService jobStatusService, IRepositoryWrapper Repository, IPublisher Publisher)
    : IRequestHandler<RefreshEPGFileRequest, APIResponse>
{
    public async Task<APIResponse> Handle(RefreshEPGFileRequest request, CancellationToken cancellationToken)
    {
        JobStatusManager jobManager = jobStatusService.GetJobManagerRefreshEPG(request.Id);
        try
        {
            if (jobManager.IsRunning)
            {
                return APIResponse.NotFound;
            }
            jobManager.Start();

            EPGFile? epgFile = await Repository.EPGFile.GetEPGFileById(request.Id).ConfigureAwait(false);
            if (epgFile == null)
            {
                jobManager.SetError();
                await messageService.SendError($"EPG with ID {request.Id} not found");
                return APIResponse.NotFound;
            }

            if (epgFile.LastDownloadAttempt.AddMinutes(epgFile.MinimumMinutesBetweenDownloads) < SMDT.UtcNow)
            {
                FileDefinition fd = FileDefinitions.EPG;
                string fullName = Path.Combine(fd.DirectoryLocation, epgFile.Source);

                if (epgFile.Url?.Contains("://") == true)
                {
                    Logger.LogInformation("Refresh EPG From URL {epgFile.Name}", epgFile.Name);

                    epgFile.LastDownloadAttempt = SMDT.UtcNow;
                    epgFile.LastUpdated = epgFile.LastDownloadAttempt;

                    (bool success, Exception? ex) = await fileUtilService.DownloadUrlAsync(epgFile.Url, fullName).ConfigureAwait(false);
                    if (!success)

                    {
                        jobManager.SetError();
                        ++epgFile.DownloadErrors;
                        Logger.LogCritical("Exception EPG From URL {ex}", ex);
                        await messageService.SendError("Exception EPG From URL {ex}", ex);
                        return APIResponse.ErrorWithMessage($"Could not get streams from M3U file {epgFile.Name}");
                    }

                    (int channelCount, int programCount) = await fileUtilService.ReadXmlCountsFromFileAsync(fullName, epgFile.EPGNumber);
                    if (channelCount == -1)
                    {
                        jobManager.SetError();
                        fileUtilService.CleanUpFile(fullName);
                        Logger.LogCritical("Exception EPG '{name}' format is not supported", epgFile.Name);
                        await messageService.SendError($"Exception EPG '{epgFile.Name}' format is not supported");
                        return APIResponse.ErrorWithMessage($"Could not get streams from M3U file {epgFile.Name}");
                    }
                    epgFile.ChannelCount = channelCount;
                    epgFile.ProgrammeCount = programCount;
                }
            }

            epgFile.DownloadErrors = 0;
            epgFile.LastDownloaded = SMDT.UtcNow;
            epgFile.FileExists = true;
            epgFile.LastUpdated = SMDT.UtcNow;

            Repository.EPGFile.UpdateEPGFile(epgFile);

            _ = await Repository.SaveAsync().ConfigureAwait(false);

            EPGFileDto toPublish = Mapper.Map<EPGFileDto>(epgFile);

            await Publisher.Publish(new EPGFileAddedEvent(toPublish), cancellationToken).ConfigureAwait(false);

            jobManager.SetSuccessful();
            await messageService.SendSuccess($"Refreshed EPG {epgFile.Name}");
            return APIResponse.Success;
        }
        catch (Exception ex)
        {
            jobManager.SetError();
            return APIResponse.ErrorWithMessage(ex, "Refresh EPG failed");
        }
    }
}
