package ob.recolor
{
    import flash.utils.ByteArray;

    import otlib.animation.FrameGroup;
    import otlib.core.ClientFeatures;
    import otlib.core.otlib_internal;
    import otlib.sprites.Sprite;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingCategory;
    import otlib.things.ThingType;
    import otlib.things.ThingTypeStorage;
    import otlib.utils.ChangeResult;
    import otlib.utils.SpriteExtent;

    use namespace otlib_internal;

    /** Stages new SPR records before publishing a DAT change. Existing sprite IDs are never overwritten. */
    public final class RecolorSave
    {
        public static function apply(things:ThingTypeStorage, sprites:SpriteStorage, features:ClientFeatures,
                                     id:uint, category:String, options:Object, masks:Object,
                                     createNew:Boolean):uint
        {
            var source:ThingType = things.getThingType(id, category);
            if (!source) throw new Error("The source object no longer exists.");
            var maxId:Number = category == ThingCategory.ITEM && features.extendedItems ? uint.MAX_VALUE : 65535;
            if (createNew && things.getMaxId(category) >= maxId)
                throw new Error("The object category has reached its ID limit.");

            var thing:ThingType = source.clone();
            var pending:Vector.<ByteArray> = new Vector.<ByteArray>();
            var slots:Array = [];
            var reused:Object = {};
            var changed:Boolean = false;
            var limit:Number = features.extended ? uint.MAX_VALUE : 65535;
            var initialCount:uint = sprites.spritesCount;
            var size:int = SpriteExtent.DEFAULT_SIZE;
            try
            {
                for each (var group:FrameGroup in thing.frameGroups)
                {
                    if (!group) continue;
                    var tiles:uint = group.width * group.height;
                    var maskLength:uint = tiles * size * size;
                    for (var slot:uint = 0; slot < group.spriteIndex.length; slot++)
                    {
                        var spriteId:uint = group.spriteIndex[slot];
                        if (!spriteId) continue;
                        var texture:uint = slot / tiles;
                        var mask:ByteArray = masks ? masks[SpriteRecolor.maskKey(group.type, texture)] as ByteArray : null;
                        if (mask && mask.length != maskLength)
                            throw new Error("The selection no longer matches the texture.");
                        // Outfit template layers encode dye channels and are kept intact.
                        var editable:Boolean = (category != ThingCategory.OUTFIT || texture % group.layers == 0)
                            && (!masks || mask != null);
                        if (!editable && !createNew) continue;

                        var reusable:Boolean = !editable || !masks;
                        var key:String = spriteId + (editable ? ":recolor" : ":original");
                        var next:uint = reusable ? uint(reused[key]) : 0;
                        if (!next)
                        {
                            var pixels:ByteArray = sprites.getPixelsStrict(spriteId);
                            var edited:ByteArray = editable ? SpriteRecolor.applyTile(pixels, options, mask, group, slot % tiles) : null;
                            if (edited) changed = true;
                            if (!edited && !createNew)
                            {
                                pixels.clear();
                                continue;
                            }
                            if (!edited)
                            {
                                key = spriteId + ":original";
                                next = uint(reused[key]);
                                reusable = true;
                            }
                            if (!next)
                            {
                                if (Number(initialCount) + pending.length >= limit)
                                    throw new Error("The SPR has reached its sprite ID limit.");
                                pending.push(edited || pixels);
                                next = initialCount + pending.length;
                                if (reusable) reused[key] = next;
                                if (edited) pixels.clear();
                            }
                            else
                                pixels.clear();
                        }
                        slots.push({group:group, index:slot, id:next});
                    }
                }
                if (!changed) return 0;

                try
                {
                    for each (pixels in pending)
                    {
                        var result:ChangeResult = sprites.internalAddSprite(pixels);
                        if (!result.done) throw new Error(result.message);
                    }
                    for each (var entry:Object in slots)
                        entry.group.spriteIndex[entry.index] = entry.id;
                    result = createNew ? things.internalAddThing(thing, category)
                        : things.internalReplaceThing(thing, category, id);
                    if (!result.done) throw new Error(result.message);
                }
                catch (error:Error)
                {
                    while (sprites._spritesCount > initialCount)
                    {
                        var sprite:Sprite = sprites._sprites[sprites._spritesCount];
                        if (sprite) sprite.dispose();
                        delete sprites._sprites[sprites._spritesCount--];
                    }
                    throw error;
                }

                things.invalidate();
                sprites.invalidate();
                return thing.id;
            }
            finally
            {
                for each (var bytes:ByteArray in pending) bytes.clear();
            }
            return 0;
        }
    }
}
