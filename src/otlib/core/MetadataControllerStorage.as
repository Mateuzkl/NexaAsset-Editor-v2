package otlib.core
{
    import otlib.things.MetadataReader;
    import otlib.things.MetadataReader1;
    import otlib.things.MetadataReader2;
    import otlib.things.MetadataReader3;
    import otlib.things.MetadataReader4;
    import otlib.things.MetadataReader5;
    import otlib.things.MetadataReader6;
    import otlib.things.MetadataWriter;
    import otlib.things.MetadataWriter1;
    import otlib.things.MetadataWriter2;
    import otlib.things.MetadataWriter3;
    import otlib.things.MetadataWriter4;
    import otlib.things.MetadataWriter5;
    import otlib.things.MetadataWriter6;

    /**
     * Singleton registry for metadata controllers.
     * "Default" uses version-based selection.
     */
    public class MetadataControllerStorage
    {
        private static var _instance:MetadataControllerStorage;

        private var _controllers:Vector.<MetadataControllerDescriptor>;
        private var _defaultController:MetadataControllerDescriptor;

        public function MetadataControllerStorage()
        {
            _controllers = new Vector.<MetadataControllerDescriptor>();

            // Register default controller (version-based selection)
            _defaultController = new MetadataControllerDescriptor("default", null, null);
            _controllers.push(_defaultController);
            _controllers.push(new MetadataControllerDescriptor("wot64", MetadataReader6, MetadataWriter6));
        }

        public static function getInstance():MetadataControllerStorage
        {
            if (!_instance)
                _instance = new MetadataControllerStorage();
            return _instance;
        }

        /**
         * Register a custom metadata controller.
         * @param name Display name for the controller
         * @param readerClass Class that extends MetadataReader
         * @param writerClass Class that extends MetadataWriter
         */
        public function register(name:String, readerClass:Class, writerClass:Class):void
        {
            // Check if already registered
            for each (var desc:MetadataControllerDescriptor in _controllers)
            {
                if (desc.name == name)
                    return;
            }
            _controllers.push(new MetadataControllerDescriptor(name, readerClass, writerClass));
        }

        /**
         * Get list of all available controllers for dropdown.
         */
        public function getList():Array
        {
            var result:Array = [];
            for each (var desc:MetadataControllerDescriptor in _controllers)
            {
                result.push(desc);
            }
            return result;
        }

        /**
         * Get controller by name.
         */
        public function getByName(name:String):MetadataControllerDescriptor
        {
            for each (var desc:MetadataControllerDescriptor in _controllers)
            {
                if (desc.name == name)
                    return desc;
            }
            return _defaultController;
        }

        /**
         * Get the default controller.
         */
        public function get defaultController():MetadataControllerDescriptor
        {
            return _defaultController;
        }

        /**
         * Create a MetadataReader based on controller name and version.
         * If "Default", uses version-based selection.
         */
        public function createReader(controllerName:String, version:uint):MetadataReader
        {
            if (controllerName == "wot64" && version <= 986) throw new Error("The Wot flag profile requires the 10.x+ DAT layout. Select a compatible output profile.");
            var desc:MetadataControllerDescriptor = getByName(controllerName);

            // If custom controller with specific class
            if (desc.readerClass != null)
            {
                return new desc.readerClass() as MetadataReader;
            }

            // Default: version-based selection
            if (version <= 730)
                return new MetadataReader1();
            else if (version <= 750)
                return new MetadataReader2();
            else if (version <= 772)
                return new MetadataReader3();
            else if (version <= 854)
                return new MetadataReader4();
            else if (version <= 986)
                return new MetadataReader5();
            else
                return new MetadataReader6();
        }

        /**
         * Create a MetadataWriter based on controller name and version.
         * If "Default", uses version-based selection.
         */
        public function createWriter(controllerName:String, version:uint):MetadataWriter
        {
            if (controllerName == "wot64" && version <= 986) throw new Error("The Wot flag profile requires the 10.x+ DAT layout. Custom flag 0x28 cannot be silently discarded.");
            var desc:MetadataControllerDescriptor = getByName(controllerName);

            // If custom controller with specific class
            if (desc.writerClass != null)
            {
                return new desc.writerClass() as MetadataWriter;
            }

            // Default: version-based selection
            if (version <= 730)
                return new MetadataWriter1();
            else if (version <= 750)
                return new MetadataWriter2();
            else if (version <= 772)
                return new MetadataWriter3();
            else if (version <= 854)
                return new MetadataWriter4();
            else if (version <= 986)
                return new MetadataWriter5();
            else
                return new MetadataWriter6();
        }
    }
}
