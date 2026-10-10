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

package otlib.core
{
    import flash.utils.IDataInput;
    import flash.utils.IDataOutput;
    import flash.utils.IExternalizable;

    /**
     * Centralized container for client feature flags.
     * Replaces scattered Boolean parameters across the codebase.
     */
    public class ClientFeatures implements IExternalizable
    {
        // --------------------------------------------------------------------------
        // PROPERTIES
        // --------------------------------------------------------------------------

        public var extended:Boolean;
        public var transparency:Boolean;
        public var improvedAnimations:Boolean;
        public var frameGroups:Boolean;
        public var metadataController:String;
        public var attributeServer:String;

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function ClientFeatures(extended:Boolean = false,
                transparency:Boolean = false,
                improvedAnimations:Boolean = false,
                frameGroups:Boolean = false,
                metadataController:String = "default",
                attributeServer:String = null)
        {
            this.extended = extended;
            this.transparency = transparency;
            this.improvedAnimations = improvedAnimations;
            this.frameGroups = frameGroups;
            this.metadataController = metadataController;
            this.attributeServer = attributeServer;
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        /**
         * Creates a copy of this ClientFeatures object.
         */
        public function clone():ClientFeatures
        {
            return new ClientFeatures(extended, transparency, improvedAnimations, frameGroups, metadataController, attributeServer);
        }

        /**
         * Copies all properties from another ClientFeatures object.
         */
        public function copyFrom(other:ClientFeatures):void
        {
            if (other)
            {
                this.extended = other.extended;
                this.transparency = other.transparency;
                this.improvedAnimations = other.improvedAnimations;
                this.frameGroups = other.frameGroups;
                this.metadataController = other.metadataController;
                this.attributeServer = other.attributeServer;
            }
        }

        /**
         * Applies version-based defaults for features.
         * Ensures minimum required features are enabled for specific client versions.
         */
        public function applyVersionDefaults(versionValue:uint):void
        {
            if (versionValue >= 960)
                extended = true;

            if (versionValue >= 1050)
                improvedAnimations = true;

            if (versionValue >= 1057)
                frameGroups = true;
        }

        /**
         * Checks if any feature differs from another ClientFeatures object.
         */
        public function differs(other:ClientFeatures):Boolean
        {
            if (!other)
                return true;

            return (extended != other.extended ||
                    transparency != other.transparency ||
                    improvedAnimations != other.improvedAnimations ||
                    frameGroups != other.frameGroups ||
                    metadataController != other.metadataController ||
                    attributeServer != other.attributeServer);
        }

        public function toString():String
        {
            return "[ClientFeatures extended=" + extended +
                ", transparency=" + transparency +
                ", improvedAnimations=" + improvedAnimations +
                ", frameGroups=" + frameGroups +
                ", metadataController=" + metadataController +
                ", attributeServer=" + attributeServer + "]";
        }

        // --------------------------------------
        // IExternalizable
        // --------------------------------------

        public function writeExternal(output:IDataOutput):void
        {
            output.writeBoolean(extended);
            output.writeBoolean(transparency);
            output.writeBoolean(improvedAnimations);
            output.writeBoolean(frameGroups);
            output.writeUTF(metadataController ? metadataController : "default");
            output.writeUTF(attributeServer ? attributeServer : "");
        }

        public function readExternal(input:IDataInput):void
        {
            extended = input.readBoolean();
            transparency = input.readBoolean();
            improvedAnimations = input.readBoolean();
            frameGroups = input.readBoolean();

            try
            {
                metadataController = input.readUTF();
                attributeServer = input.readUTF();
            }
            catch (e:Error)
            {
                metadataController = "default";
                attributeServer = null;
            }
        }

        // --------------------------------------------------------------------------
        // STATIC
        // --------------------------------------------------------------------------

        /**
         * Creates a ClientFeatures instance from individual boolean values.
         * Convenience factory method for backward compatibility.
         */
        public static function create(extended:Boolean = false,
                transparency:Boolean = false,
                improvedAnimations:Boolean = false,
                frameGroups:Boolean = false,
                metadataController:String = "default",
                attributeServer:String = null):ClientFeatures
        {
            return new ClientFeatures(extended, transparency, improvedAnimations, frameGroups, metadataController, attributeServer);
        }

        /**
         * Creates a ClientFeatures instance from a window object.
         * Works with OpenAssetsWindow, CompileAssetsWindow, CreateAssetsWindow, MergeAssetsWindow, etc.
         * @param window Any object with optional properties: extended, transparency, improvedAnimations, frameGroups, metadataController
         */
        public static function fromWindow(window:Object):ClientFeatures
        {
            return new ClientFeatures(
                    ("extended" in window) ? window.extended : false,
                    ("transparency" in window) ? window.transparency : false,
                    ("improvedAnimations" in window) ? window.improvedAnimations : false,
                    ("frameGroups" in window) ? window.frameGroups : false,
                    ("metadataController" in window) ? window.metadataController : "default",
                    ("attributeServer" in window) ? window.attributeServer : null
                );
        }
    }
}
