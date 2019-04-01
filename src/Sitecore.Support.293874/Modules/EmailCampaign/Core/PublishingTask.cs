namespace Sitecore.Support.Modules.EmailCampaign.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Configuration;
    using Sitecore.Data;
    using Sitecore.Data.Items;
    using Diagnostics;
    using ExM.Framework.Diagnostics;
    using Publishing;
    using SecurityModel;
    using Sitecore.Modules.EmailCampaign.Core;
    using Sitecore.Modules.EmailCampaign.Core.Extensions;

    public class PublishingTask : IPublishingTask
    {
        private readonly Item _dataItem;
        private readonly ILogger _logger;
        private bool _published;
        private Handle _handle;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishingTask"/> class. 
        /// </summary>
        /// <param name="item"> The item  </param>
        /// <param name="logger"> The logger. </param>
        public PublishingTask([NotNull]Item item, [NotNull] ILogger logger)
        {
            Assert.ArgumentNotNull(item, "item");
            Assert.ArgumentNotNull(logger, "logger");
            Assert.IsNotNull(item, "item");
            this._dataItem = item;
            this._logger = logger;
        }

        /// <summary>
        /// Gets or sets a value indicating whether task should publish related items
        /// </summary>
        public bool PublishRelatedItems { get; set; }

        /// <summary>
        /// PublishAsync the item.
        /// </summary>
        public void PublishAsync()
        {
            if (this._published)
            {
                throw new InvalidOperationException("The item " + this._dataItem.ID + " is already published");
            }

            this._published = true;

            var targets = this.GetTargets(this._dataItem).ToArray();

            var unpublishedParentFolder = this.FindUnpublishedParentFolder(this._dataItem, targets);
            var itemToPublish = unpublishedParentFolder ?? this._dataItem;

            _handle = PublishManager.PublishItem(
              itemToPublish,
              targets,
              new[] { itemToPublish.Language },
              true /* deep */,
              true /* compareRevisions */,
              PublishRelatedItems /* publishRelatedItems */);
        }

        /// <summary>
        /// Freezes current thread until the publishing process is completed
        /// </summary>
        public void WaitForCompletion()
        {
            PublishManager.WaitFor(_handle);
        }

        /// <summary>
        /// Gets the targets.
        /// </summary>
        /// <param name="item">The item to publish.</param>
        /// <returns>
        /// The targets.
        /// </returns>
        private IEnumerable<Database> GetTargets(Item item)
        {
            using (new SecurityDisabler())
            {
                var publishingTargetsItem = item.Database.GetItem("/sitecore/system/publishing targets");
                if (publishingTargetsItem == null)
                {
                    yield break;
                }

                foreach (Item baseItem in publishingTargetsItem.Children)
                {
                    var targetDatabase = baseItem["Target database"];
                    if (string.IsNullOrEmpty(targetDatabase))
                    {
                        continue;
                    }

                    var database = Factory.GetDatabase(targetDatabase, false);

                    if (database != null)
                    {
                        yield return database;
                    }
                    else
                    {
                        this._logger.LogWarn("Unknown database in PublishAction: " + targetDatabase);
                    }
                }
            }
        }

        /// <summary>
        /// Finds the unpublished parent folder.
        /// </summary>
        /// <param name="item">The item for which search is performed</param>
        /// <param name="targets"> The target databases where search should be performed. </param>
        /// <returns> The <see cref="Item"/> if parent folder was found, otherwise <c>null</c>. </returns>
        [CanBeNull]
        private Item FindUnpublishedParentFolder([NotNull] Item item, [NotNull] Database[] targets)
        {
            Assert.ArgumentNotNull(item, "item");
            Assert.ArgumentNotNull(targets, "targets");
            Item unpublishedParentFolder = null;
            foreach (var folderItem in item.GetParentItems(new TemplateID(TemplateIDs.Folder), new TemplateID(TemplateIDs.MediaFolder)))
            {
                if (IsItemPublished(folderItem, targets))
                {
                    break;
                }

                unpublishedParentFolder = folderItem;
            }

            return unpublishedParentFolder;
        }

        private static bool IsItemPublished(Item item, Database[] targets)
        {
            return targets.All(t => t.GetItem(item.ID) != null);
        }
    }
}