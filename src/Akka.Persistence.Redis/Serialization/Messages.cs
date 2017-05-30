namespace Akka.Persistence.Redis.Serialization
{
    public class PersistenceMessage
    {
        public string PersistenceId { get; set; }
        public long SequenceNr { get; set;}
        public string WriterGuid { get; set; }
        public PersistencePayload Payload { get; set; } = new PersistencePayload();
    }

    public class SnapshotMessage
    {
        public string PersistenceId { get; }
        public long SequenceNr { get ; }
        public long TimeStamp { get; }
        public PersistencePayload Payload { get; }
    }

    public class PersistencePayload
    {
        public int SerializerId { get; set; }
        public string Manifest { get; set;}
        public byte[] Payload { get; set; }
    }
}


