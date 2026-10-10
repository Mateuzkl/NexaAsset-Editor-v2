package otlib.utils
{
    import otlib.animation.FrameGroup;
    import otlib.things.FrameGroupType;
    import otlib.things.ThingType;

    /** Clone only after every nonzero source sprite has a destination mapping. */
    public final class ThingSpriteMap
    {
        public static function remap(source:ThingType, mapping:Object):ThingType
        {
            var result:ThingType = source.clone();
            for (var type:uint = FrameGroupType.DEFAULT; type <= FrameGroupType.WALKING; type++)
            {
                var group:FrameGroup = result.getFrameGroup(type);
                if (!group || !group.spriteIndex) continue;
                for (var i:uint = 0; i < group.spriteIndex.length; i++)
                {
                    var id:uint = group.spriteIndex[i];
                    if (!id) continue;
                    if (!mapping.hasOwnProperty(id.toString())) throw new Error("Missing sprite mapping: " + id);
                    group.spriteIndex[i] = mapping[id.toString()];
                }
            }
            return result;
        }
    }
}
