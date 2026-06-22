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
            Target originTarget,
            ushort originLinkKey,
            Entity targetEntity,
            in UnsafeComponentLookup<Targets> targetsLookup,
            in UnsafeComponentLookup<EntityLinkSource> linkSources,
            in UnsafeBufferLookup<EntityLinkEntry> links)
        {
            if (originTarget == Target.None || originTarget == Target.Self)
                return targetEntity;

            var targets = targetsLookup.TryGetComponent(targetEntity, out var t) ? t : default;
            var baseTarget = targets.Get(originTarget, targetEntity);
            if (baseTarget == Entity.Null)
                return targetEntity;

            if (originLinkKey != 0 &&
                EntityLinkResolver.TryResolve(baseTarget, originLinkKey, linkSources, links, out var linked))
                return linked;

            return baseTarget;
        }
    }
}
