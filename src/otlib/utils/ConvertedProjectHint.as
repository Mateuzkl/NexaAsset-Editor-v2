package otlib.utils
{
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import otlib.things.ThingTypeStorage;
    import otlib.things.ThingType;
    import otlib.animation.FrameGroup;

    /** A bounded, signature-checked selection hint. No paths or commands are accepted. */
    public final class ConvertedProjectHint
    {
        public static function read(directory:File, things:ThingTypeStorage, version:uint, sprSignature:uint):Object
        {
            var file:File=directory.resolvePath("nexa-project.json"),stream:FileStream=new FileStream();
            if(!file.exists || file.isDirectory || file.size>16384)return null;
            try {
                stream.open(file,FileMode.READ);var value:Object=JSON.parse(stream.readUTFBytes(stream.bytesAvailable));
                if(value.schemaVersion!==1 || value.version!==Number(version) || value.datSignature!==Number(things.signature) || value.sprSignature!==Number(sprSignature))return null;
                var category:String=value.initialSelection.category,id:Number=value.initialSelection.id;
                if(["item","outfit","effect","missile"].indexOf(category)<0 || !isFinite(id) || id!=Math.floor(id) || id<things.getMinId(category) || id>things.getMaxId(category))return null;
                var thing:ThingType=things.getThingType(uint(id),category);
                for each(var group:FrameGroup in thing.frameGroups)if(group)for each(var sprite:uint in group.spriteIndex)if(sprite>0)return {category:category,id:uint(id),sprite:sprite};
            }catch(error:Error){return null;}finally{stream.close();}
            return null;
        }
    }
}
