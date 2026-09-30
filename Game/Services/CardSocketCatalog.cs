using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The socketing templates of the item resources, frozen once at startup so a socketing request never
/// queries the resource table. Built the same way as <see cref="ItemUseCatalog"/> and
/// <see cref="ItemGroupCatalog"/>.
/// </summary>
public class CardSocketCatalog : ICardSocketCatalog
{
    private readonly ILogger _logger = Log.ForContext<CardSocketCatalog>();
    private readonly FrozenDictionary<int, ItemSocketTemplate> _templates;

    public CardSocketCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetSocketFields();
        var templates = new Dictionary<int, ItemSocketTemplate>(fields.Count);

        foreach (var field in fields)
        {
            templates[field.Id] = new ItemSocketTemplate(field.SocketCount,
                CardSocketRules.IsSoulstone(field.Group, field.ItemType, field.BaseType));
        }

        _templates = templates.ToFrozenDictionary();
        _logger.Debug("Loaded socket templates for {count} item resources", _templates.Count);
    }

    public bool TryGetTemplate(long itemResourceId, out ItemSocketTemplate template)
    {
        return _templates.TryGetValue((int)itemResourceId, out template);
    }
}
