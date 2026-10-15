package ob.modern
{
    import flash.events.Event;
    public class ModernFlagEvent extends Event
    {
        public static const CHANGE:String="modernFlagChange";
        public var number:int;
        public var enabled:Boolean;
        public var instances:Array;
        public function ModernFlagEvent(number:int,enabled:Boolean,instances:Array=null){super(CHANGE,true);this.number=number;this.enabled=enabled;this.instances=instances;}
        override public function clone():Event{return new ModernFlagEvent(number,enabled,instances);}
    }
}
