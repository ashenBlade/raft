using Consensus.StateMachine.JobQueue.Commands;
using Consensus.StateMachine.JobQueue.Commands.Batch;
using Consensus.StateMachine.JobQueue.Commands.Dequeue;
using Consensus.StateMachine.JobQueue.Commands.Enqueue;
using Consensus.StateMachine.JobQueue.Commands.GetCount;

namespace Consensus.StateMachine.JobQueue.Serialization;

public class JobQueueRequestSerializer : IJobQueueRequestSerializer
{
    public static readonly JobQueueRequestSerializer Instance = new();

    public void Serialize(IJobQueueRequest request, BinaryWriter writer)
    {
        request.Accept(new SerializerVisitor(writer));
    }

    private class SerializerVisitor : IJobQueueRequestVisitor
    {
        private readonly BinaryWriter _writer;

        public SerializerVisitor(BinaryWriter writer)
        {
            _writer = writer;
        }
        
        public void Visit(DequeueRequest request)
        {
            _writer.Write((int)RequestType.DequeueRequest);
        }

        public void Visit(EnqueueRequest request)
        {
            _writer.Write((int)RequestType.EnqueueRequest);
            _writer.Write(request.Key);
            _writer.Write(request.Payload.Length);
            _writer.Write(request.Payload);
        }

        public void Visit(GetCountRequest request)
        {
            _writer.Write((int)RequestType.GetCountRequest);
        }

        public void Visit(BatchRequest request)
        {
            _writer.Write((int)RequestType.BatchRequest);
            _writer.Write(request.Requests.Count);
            foreach (var innerRequest in request.Requests)
            {
                innerRequest.Accept(this);
            }
        }
    }
}