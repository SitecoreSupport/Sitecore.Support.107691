using System;
using System.Linq;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Links;
using Sitecore.PathAnalyzer.Data.Models;
using Sitecore.PathAnalyzer.Services.PathExplorer.ViewModels;
using Sitecore.PathAnalyzer.Services.ViewModels;
using Sitecore.SecurityModel;
using Sitecore.SequenceAnalyzer;
using Sitecore.PathAnalyzer.Data.SitecoreData;
using Sitecore.PathAnalyzer.Localization;
using System.Collections.Generic;
using Sitecore.StringExtensions;
using Sitecore.PathAnalyzer.Services.Data;
using Sitecore.PathAnalyzer.Services;

namespace Sitecore.Support.PathAnalyzer.Services.Data
{
  /// <summary>A node factory.</summary>
  /// <seealso cref="T:Sitecore.PathAnalyzer.Services.ViewModels.INodeFactory"/>
  public class NodeFactory : INodeFactory
  {
    private readonly IItemRepository _itemRepository;
    private readonly IResourceManager _resourceManager;
    #region Modified code
    // The cache should include both ID and Name of the node, or the all wildcard items will have the same name
    private readonly Dictionary<string, string> nodeNameCache = new Dictionary<string, string>();
    #endregion
    private static readonly NodeNameResolvingMode NameResolvingMode = ApiContainer.GetSettings().NodeNameResolvingMode;

    /// <summary>
    /// Initializes a new instance of the Sitecore.PathAnalyzer.Services.ViewModels.NodeFactory
    /// class.
    /// </summary>
    /// <param name="itemRepository">The item repository.</param>
    /// <param name="resourceManager">The resource manager.</param>
    public NodeFactory(IItemRepository itemRepository, IResourceManager resourceManager)
    {
      Assert.Required(itemRepository, "item repository required");
      Assert.Required(resourceManager, "resource manager required");

      _itemRepository = itemRepository;
      _resourceManager = resourceManager;
    }

    /// <summary>Builds master node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>A MasterNode.</returns>
    /// <seealso cref="M:Sitecore.PathAnalyzer.Services.ViewModels.INodeFactory.CreateMasterNode(Node,int)"/>
    public virtual MasterNode CreateMasterNode(Node node)
    {
      Assert.ArgumentNotNull(node, "node");

      Item item;
      using (new SecurityDisabler())
      {
        item = _itemRepository.GetItem(node.RecordId, string.Empty, string.Empty, string.Empty);
      }

      // root node
      if (node.Depth == 0 && node.RecordId == Guid.Empty)
      {
        return new MasterNode
        {
          Id = node.Id,
          Name = Localizer.ResourceManager.Translate(PathAnalyzerTexts.Internet),
          RecordId = node.RecordId,
          SubNodes = node.Children.ToList()
        };
      }

      var masterNode = new MasterNode
      {
        Id = node.Id,
        Name = node.Name,
        RecordId = node.RecordId,
        SubNodes = node.Children.ToList()
      };

      if (item != null)
      {
        masterNode.Name = ResolveName(node, item);
        masterNode.TemplateName = item.TemplateName;
        masterNode.Url = ResolveUrl(node, item, new UrlOptions());
        masterNode.ContentPath = item.Paths.ContentPath;
      }

      return masterNode;
    }

    /// <summary>Creates node view model.</summary>
    /// <param name="node">The node.</param>
    /// <param name="includeChildren">Whether or not child nodes should be included.</param>
    /// <returns>The new node view model.</returns>
    /// <seealso cref="M:Sitecore.PathAnalyzer.Services.ViewModels.INodeFactory.CreateNodeViewModel(Node,int)"/>
    public virtual NodeViewModel CreateNodeViewModel(Node node, bool includeChildren = true)
    {
      var experienceNode = node as ExperienceNode;
      Assert.IsNotNull(experienceNode, "experienceNode != null");

      var duration = 0;
      var pageNode = node as PageNode;
      if (pageNode != null)
      {
        duration = pageNode.AverageDuration;
      }

      var nodeVm = new NodeViewModel
      {
        Id = node.Id,
        RecordId = node.RecordId,
        Name = ResolveName(node),
        PruneCount = node.PruneCount,
        SubtreeValue = node.SubtreeValue,
        SubtreeCount = node.SubtreeCount,
        PruneValue = node.PruneValue,
        ExitCount = node.ExitCount,
        ExitValue = node.ExitValue,
        ExitValuePotential = node.CalculateExitPotential(),
        OutcomeCount = experienceNode.OutcomeCount,
        MonetaryValue = experienceNode.MonetaryValue,
        AverageMonetaryValue = experienceNode.AverageMonetaryValue,
        Duration = duration,
        Children = includeChildren ? node.Children.Select(n => CreateNodeViewModel(n)) : null
      };

      return nodeVm;
    }

    /// <summary>Creates explorer node.</summary>
    /// <param name="sourceNode">   Source node.</param>
    /// <returns>The new explorer node.</returns>
    public virtual ExplorerNode CreateExplorerNode(Node sourceNode)
    {
      Assert.IsTrue(sourceNode is PageNode, "source node is not of expected type (PageNode)");

      var node = sourceNode as PageNode;

      var masterNode = CreateMasterNode(sourceNode);

      var explorerNode = new ExplorerNode
      {
        Id = masterNode.Id,
        Name = masterNode.Name,
        Url = masterNode.Url,

        ItemId = node.RecordId,
        PruneCount = node.PruneCount,
        SubtreeValue = node.SubtreeValue,
        SubtreeCount = node.SubtreeCount,
        PruneValue = node.PruneValue,
        ExitCount = node.ExitCount,
        ExitValue = node.ExitValue,
        MonetaryValue = node.MonetaryValue,
        AverageMonetaryValue = node.AverageMonetaryValue,
        OutcomeCount = node.OutcomeCount,
        TimeSpent = node.AverageDuration,
        Children = masterNode.SubNodes.Select(CreateExplorerNode).ToList()
      };

      return explorerNode;
    }

    #region Protected methods

    /// <summary>Resolve name.</summary>
    /// <param name="node">The node.</param>
    /// <returns>A string.</returns>
    protected virtual string ResolveName([NotNull] Node node)
    {
      Assert.ArgumentNotNull(node, "node");

      // root
      if (node.Depth == 0 && node.RecordId.Equals(Guid.Empty))
      {
        return Localizer.ResourceManager.Translate(PathAnalyzerTexts.Internet);
      }

      #region Modified code
      // Update the cache to use string intead of ID, because the node name is now included as a cache key
      if (this.nodeNameCache.ContainsKey(node.RecordId.ToString() + node.Name))
      {
        return this.nodeNameCache[node.RecordId.ToString() + node.Name];
      }
      #endregion
      var nodeName = ResolveNodeName(node);

      // do not cache not found nodes since their names may be different
      #region Modified code
      // Update the cache to use string intead of ID, because the node name is now included as a cache key
      if (node.RecordId != Guid.Empty)
      {
        this.nodeNameCache.Add(node.RecordId.ToString() + node.Name, nodeName);
      }
      #endregion
      var groupSuffix = string.Empty;
      if (node.IsGroupedNode && (node.MergedNodeCount > 1))
      {
        groupSuffix = _resourceManager.Translate(PathAnalyzerTexts.NodeGroups, new object[] { node.MergedNodeCount });
      }

      var separator = string.IsNullOrEmpty(groupSuffix) ? string.Empty : " ";
      return "{0}{1}{2}".FormatWith(nodeName, separator, groupSuffix);
    }

    private string ResolveNodeName(Node node)
    {
      var nodeName = string.Empty;
      switch (NameResolvingMode)
      {
        case NodeNameResolvingMode.Raw:
          nodeName = ResolveFromRawNodeName(node);
          // some reverse maps may have empty root node name
          if (nodeName.IsNullOrEmpty())
          {
            nodeName = ResolveNodeNameFromItem(node);
          }
          break;
        case NodeNameResolvingMode.Name:
          {
            nodeName = ResolveNodeNameFromItem(node);
          }
          break;
        case NodeNameResolvingMode.DisplayName:
          {
            var item = GetNodeItem(node);
            nodeName = item != null ? item.DisplayName : ResolveFromRawNodeName(node);
          }
          break;
      }

      // falling back to raw name if can't resolve properly
      return !nodeName.IsNullOrEmpty() ? nodeName : node.Name;
    }

    private string ResolveNodeNameFromItem([NotNull]Node node)
    {
      #region Modified code
      var item = GetNodeItem(node);
      if (item == null)
      {
        return this.ResolveFromRawNodeName(node);
      }

      // check if it is a wildcard item
      if (item.Name != "*")
      {
        return item.Name;
      }
      else
      {
        // use a part of URL instead of name. Also need to remove the "/" symbol for consistent item names
        return new Uri("http://localhost" + node.Name).Segments.LastOrDefault().Replace("/", "");
      }
      #endregion
    }

    private Item GetNodeItem([NotNull]Node node)
    {
      return _itemRepository.GetItem(node.RecordId, string.Empty, string.Empty, string.Empty);
    }

    private string ResolveFromRawNodeName([NotNull]INode node)
    {
      Assert.ArgumentNotNull(node, "node != null");

      var name = node.Name.Split('?').FirstOrDefault();
      if (name.IsNullOrEmpty())
      {
        return node.Name;
      }

      return name.Equals("/", StringComparison.InvariantCultureIgnoreCase) ?
          Localizer.ResourceManager.Translate(Texts.HOME) :
          System.IO.Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>Resolve name.</summary>
    /// <param name="node">The node.</param>
    /// <param name="item">The source item.</param>
    /// <returns>A string.</returns>
    protected virtual string ResolveName([NotNull] Node node, Item item)
    {
      Assert.ArgumentNotNull(node, "node");

      var groupSuffix = string.Empty;
      if (node.IsGroupedNode && (node.MergedNodeCount > 1))
      {
        groupSuffix = _resourceManager.Translate(PathAnalyzerTexts.NodeGroups, new object[] { node.MergedNodeCount });
      }

      var nodeName = item != null ? item.DisplayName : new Uri("http://localhost" + node.Name).Segments.LastOrDefault();
      if (!string.IsNullOrEmpty(nodeName) && nodeName.Equals("/", StringComparison.InvariantCultureIgnoreCase))
      {
        return Localizer.ResourceManager.Translate(Texts.HOME);
      }

      var separator = string.IsNullOrEmpty(groupSuffix) ? string.Empty : " ";
      return "{0}{1}{2}".FormatWith(nodeName, separator, groupSuffix);
    }

    /// <summary>Resolve URL.</summary>
    /// <param name="node">The node.</param>
    /// <param name="item">The source item.</param>
    /// <param name="urlOptions">Options for controlling the URL.</param>
    /// <returns>A string.</returns>
    protected virtual string ResolveUrl([NotNull] Node node, Item item, UrlOptions urlOptions)
    {
      Assert.ArgumentNotNull(node, "node");
      return item != null ? LinkManager.GetItemUrl(item, urlOptions) : node.Name;
    }

    #endregion
  }
}