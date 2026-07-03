using System.Runtime.CompilerServices;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public static class OriginResolution
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity TryResolveOrigin(
            in EntityLinkRef origin,
            Entity targetEntity,
            in UnsafeComponentLookup<Targets> targetsLookup,
            in UnsafeComponentLookup<EntityLinkSource> linkSources,
            in UnsafeBufferLookup<EntityLinkEntry> links)
        {
            var targets = targetsLookup.TryGetComponent(targetEntity, out var t) ? t : default;
            return origin.TryResolve(targetEntity, targets, linkSources, links, out var e) ? e : targetEntity;
        }
    }
}
