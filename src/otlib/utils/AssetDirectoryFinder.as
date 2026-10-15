/*
*  Copyright (c) 2014-2023 Object Builder <https://github.com/ottools/ObjectBuilder>
*
*  Permission is hereby granted, free of charge, to any person obtaining a copy
*  of this software and associated documentation files (the "Software"), to deal
*  in the Software without restriction, including without limitation the rights
*  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
*  copies of the Software, and to permit persons to whom the Software is
*  furnished to do so, subject to the following conditions:
*
*  The above copyright notice and this permission notice shall be included in
*  all copies or substantial portions of the Software.
*
*  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
*  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
*  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
*  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
*  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
*  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
*  THE SOFTWARE.
*/

package otlib.utils
{
    import flash.filesystem.File;
    import otlib.sprites.SpriteReader;

    /** Resolves one nested client folder without walking an entire disk tree. */
    public final class AssetDirectoryFinder
    {
        private static const MAX_DIRECTORIES:uint = 512;
        private static const MAX_DEPTH:uint = 2;

        public static function resolve(selected:File):File
        {
            if (!selected || !selected.exists || !selected.isDirectory) return selected;
            var level:Array = [selected];
            var visited:uint = 0;
            for (var depth:uint = 0; depth <= MAX_DEPTH && level.length; depth++)
            {
                var matches:Array = [];
                var next:Array = [];
                for each (var directory:File in level)
                {
                    visited++;
                    if (visited > MAX_DIRECTORIES) return selected;
                    if (hasClientAssets(directory)) matches.push(directory);
                    if (depth < MAX_DEPTH)
                    {
                        for each (var child:File in directory.getDirectoryListing())
                            if (child.isDirectory) next.push(child);
                    }
                }
                if (matches.length == 1) return matches[0];
                if (matches.length > 1) return selected;
                level = next;
            }
            return selected;
        }

        private static function hasClientAssets(directory:File):Boolean
        {
            var dat:File = directory.resolvePath("Tibia.dat");
            var spr:File = directory.resolvePath("Tibia.spr");
            if (!spr.exists) spr = SpriteReader.findMultiSpriteFile(directory);
            if (!spr)
            {
                for each (var entry:File in directory.getDirectoryListing())
                {
                    if (!entry.isDirectory && entry.extension.toLowerCase() == "spr" &&
                            !SpriteReader.isFilePartsEntry(entry))
                    {
                        spr = entry;
                        break;
                    }
                }
            }
            return dat.exists && !dat.isDirectory && spr != null && spr.exists && !spr.isDirectory;
        }
    }
}
