using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HiResLogViewer
{
    public class ControllerEvent
    {
        /// <summary>
        ///     Generic Timestamp/eventCode event.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <param name="eventcode"></param>
        public ControllerEvent(DateTime timestamp, byte eventCode)
        {
            EventCode = eventCode;
            TimeStamp = timestamp;
        }

        /// <summary>
        ///     Alternate Constructor that can handle all four peices of a controller event
        /// </summary>
        /// <param name="signalId"></param>
        /// <param name="timeStamp"></param>
        /// <param name="eventCode"></param>
        /// <param name="eventParam"></param>
        public ControllerEvent(int SignalID, DateTime TimeStamp, byte EventCode, byte EventParam)
        {
            this.SignalID = SignalID;
            this.TimeStamp = TimeStamp;
            this.EventCode = EventCode;
            this.EventParam = EventParam;
        }

        public DateTime TimeStamp { get; set; }

        public int SignalID { get; }

        public byte EventCode { get; }

        public byte EventParam { get; }


        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType()) return false;
            var y = (ControllerEvent)obj;
            return this != null && y != null && SignalID == y.SignalID && TimeStamp == y.TimeStamp
                   && EventCode == y.EventCode && EventParam == y.EventParam
                ;
        }


        public override int GetHashCode()
        {
            return this == null
                ? 0
                : SignalID.GetHashCode() ^ TimeStamp.GetHashCode() ^ EventCode.GetHashCode() ^ EventParam.GetHashCode();
        }
    }
}
