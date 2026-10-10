package ob.ui
{
    import flash.events.Event;
    /** UI-only command routing to the application's existing handlers. */
    public class NexaUiEvent extends Event
    {
        public static const ACTION:String = "nexaAction";
        public var action:String;
        public function NexaUiEvent(action:String)
        {
            super(ACTION, true);
            this.action = action;
        }
        override public function clone():Event { return new NexaUiEvent(action); }
    }
}
