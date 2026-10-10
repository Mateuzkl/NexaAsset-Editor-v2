package otlib.utils
{
    import flash.filesystem.File;

    /** Stage a complete archive, retain the replaced files, roll back ordinary I/O failures. */
    public final class AssetOutputTransaction
    {
        public static function createStage(root:File):File
        {
            root.createDirectory();
            var stage:File = root.resolvePath(".ob-stage-" + token());
            if (stage.exists) throw new Error("Asset staging directory already exists.");
            stage.createDirectory();
            return stage;
        }

        public static function publish(stage:File, root:File, names:Array):File
        {
            var name:String;
            var seen:Object = {};
            for each (name in names)
            {
                // Caller supplies immediate, generated archive names, never arbitrary paths.
                if (!name || name == "." || name == ".." || /[\\\/:]/.test(name))
                    throw new Error("Invalid asset publication name.");
                if (seen[name.toLowerCase()]) throw new Error("Duplicate asset publication name.");
                seen[name.toLowerCase()] = true;
                if (!stage.resolvePath(name).exists) throw new Error("Missing staged asset: " + name);
            }
            var backup:File = root.resolvePath(".ob-backup-" + token());
            if (backup.exists) throw new Error("Asset backup directory already exists.");
            var saved:Array = [], installed:Array = [];
            try
            {
                for each (name in names)
                {
                    var old:File = root.resolvePath(name);
                    if (old.exists)
                    {
                        backup.createDirectory();
                        moveChecked(old, backup.resolvePath(name));
                        saved.push(name);
                    }
                }
                for each (name in names)
                {
                    moveChecked(stage.resolvePath(name), root.resolvePath(name));
                    installed.push(name);
                }
            }
            catch (error:Error)
            {
                try
                {
                    while (installed.length)
                    {
                        var installedName:String = installed.pop();
                        moveChecked(root.resolvePath(installedName), stage.resolvePath(installedName));
                    }
                    while (saved.length)
                    {
                        var savedName:String = saved.pop();
                        moveChecked(backup.resolvePath(savedName), root.resolvePath(savedName));
                    }
                }
                catch (rollbackError:Error)
                {
                    throw new Error(error.message + " Recovery files retained at " + backup.nativePath +
                        " and " + stage.nativePath + ": " + rollbackError.message);
                }
                throw error;
            }
            // Keep the complete previous generation, not a mixture of replaced parts.
            return saved.length ? backup : null;
        }

        private static function token():String
        {
            return new Date().time.toString(16) + "-" + uint(Math.random() * uint.MAX_VALUE).toString(16);
        }

        private static function moveChecked(source:File, destination:File):void
        {
            var original:File = new File(source.nativePath);
            source.moveTo(destination);
            // AIR can return without throwing for a Windows sharing violation.
            if (original.exists || !destination.exists)
                throw new Error("Unable to move asset (it may be open in another program): " + original.nativePath);
        }
    }
}
