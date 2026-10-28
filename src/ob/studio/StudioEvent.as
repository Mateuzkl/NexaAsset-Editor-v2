package ob.studio {
    import flash.events.Event;
    public class StudioEvent extends Event {
        public var message:Object;
        public function StudioEvent(value:Object){super("studioChanged");message=value;}
        override public function clone():Event{return new StudioEvent(message);}
    }
}
