package ob.modern
{
    import flash.events.Event;
    public class ModernAssetEvent extends Event
    {
        public static const MESSAGE:String = "modernAssetMessage";
        public var message:Object;
        public function ModernAssetEvent(message:Object) { super(MESSAGE); this.message = message; }
        override public function clone():Event { return new ModernAssetEvent(message); }
    }
}
