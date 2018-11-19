namespace Sitecore.Support.EmailCampaign.Cm.Pipelines.DispatchNewsletter
{
  using System;
  using System.Linq;
  using Sitecore.Analytics;
  using Sitecore.Data;
  using Sitecore.Diagnostics;
  using Sitecore.Modules.EmailCampaign.Core;
  using Sitecore.Modules.EmailCampaign.Messages;
  using Sitecore.Modules.EmailCampaign.Services;
  using Sitecore;
  using Sitecore.ExM.Framework.Diagnostics;
  using Sitecore.SecurityModel;
  using Sitecore.EmailCampaign.Cm.Pipelines.DispatchNewsletter;
  using System.Collections.Generic;

  public class PublishDispatchItems
  {
    private readonly ItemUtilExt _itemUtil;

    private readonly ILogger logger;

    private readonly IExmCampaignService _exmCampaignService;

    public PublishDispatchItems([NotNull] ItemUtilExt util, [NotNull] ILogger logger, [NotNull] IExmCampaignService exmCampaignService)
    {
      Assert.ArgumentNotNull(util, "util");
      Assert.ArgumentNotNull(logger, "logger");
      Assert.ArgumentNotNull(exmCampaignService, nameof(exmCampaignService));

      this._itemUtil = util;
      this.logger = logger;
      _exmCampaignService = exmCampaignService;
    }

    public void Process([NotNull] DispatchNewsletterArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      if (args.IsTestSend || args.SendingAborted || args.Queued)
      {
        return;
      }

      try
      {
        using (new SecurityDisabler())
        {

          var campaignTask = this.PublishCampaign(args.Message);
          var messageTasks = this.PublishMessage(args.Message);

          var publishingTasks = messageTasks.Concat(new IPublishingTask[] { campaignTask });

          foreach (var publishingTask in publishingTasks.Where(publishingTask => publishingTask != null))
          {
            publishingTask.WaitForCompletion();
          }
        }
      }
      catch (Exception e)
      {
        args.AbortSending(e, true, this.logger);
        args.AbortPipeline();
      }
    }

    protected virtual IList<IPublishingTask> PublishMessage([NotNull]MessageItem message)
    {
      Assert.ArgumentNotNull(message, "message");

      List<IPublishingTask> publishingTasks = new List<IPublishingTask>();

      var publishingTask = new PublishingTask(message.InnerItem, this.logger)
      {
        PublishRelatedItems = true
      };
      publishingTask.PublishAsync();
      publishingTasks.Add(publishingTask);

      if (message.TargetLanguage.Name != "en")
      {
        var item = Database.GetDatabase("master").GetItem(message.InnerItem.ID, message.TargetLanguage);

        var publishingTaskinOtherLanguage = new PublishingTask(item, this.logger)
        {
          PublishRelatedItems = true
        };

        publishingTaskinOtherLanguage.PublishAsync();
        publishingTasks.Add(publishingTaskinOtherLanguage);
      }

      return publishingTasks;
    }

    protected virtual IPublishingTask PublishCampaign([NotNull]MessageItem message)
    {
      Assert.ArgumentNotNull(message, "message");

      var messageCampaignId = message.CampaignId;
      if (ID.IsNullOrEmpty(messageCampaignId))
      {
        messageCampaignId = _exmCampaignService.GetMessageItem(message.MessageId).CampaignId;
      }

      var campaignItem = this._itemUtil.GetItem(messageCampaignId);
      if (campaignItem == null || campaignItem.TemplateID != AnalyticsIds.Campaign)
      {
        return null;
      }

      var publishingTask = new PublishingTask(campaignItem, this.logger);
      publishingTask.PublishAsync();

      return publishingTask;
    }
  }
}