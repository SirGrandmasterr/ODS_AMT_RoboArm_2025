using System.IO;
using Google.Protobuf;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;

namespace Unity.MLAgents.Demonstrations
{
    /// <summary>
    /// Responsible for writing demonstration data to stream (typically a file stream).
    /// </summary>
    /// <seealso cref="DemonstrationRecorder"/>
    public class DemonstrationWriter
    {
        /// <summary>
        /// Number of bytes reserved for the <see cref="DemonstrationMetaData"/> at the start of the demo file.
        /// </summary>
        internal const int MetaDataBytes = 32;

        DemonstrationMetaData m_MetaData;
        Stream m_Writer;
        float m_CumulativeReward;
        ObservationWriter m_ObservationWriter = new ObservationWriter();

        /// <summary>
        /// Create a DemonstrationWriter that will write to the specified stream.
        /// The stream must support writes and seeking.
        /// </summary>
        /// <param name="stream"></param>
        public DemonstrationWriter(Stream stream)
        {
            m_Writer = stream;
        }

        /// <summary>
        /// Number of steps written so far.
        /// </summary>
        internal int NumSteps
        {
            get { return m_MetaData.numberSteps; }
        }

        /// <summary>
        /// Writes the initial data to the stream.
        /// </summary>
        /// <param name="demonstrationName">Base name of the demonstration file(s).</param>
        /// <param name="brainName">The name of the Brain the agent is attached to.</param>
        /// <param name="brainParameters">The parameters of the Brain the agent is attached to.</param>
        internal void Initialize(
            string demonstrationName, BrainParameters brainParameters, string brainName)
        {
            if (m_Writer == null)
            {
                // Already closed
                return;
            }

            m_MetaData = new DemonstrationMetaData { demonstrationName = demonstrationName };
            var metaProto = m_MetaData.ToProto();
            metaProto.WriteDelimitedTo(m_Writer);

            WriteBrainParameters(brainName, brainParameters);
        }

        /// <summary>
        /// Writes meta-data. Note that this is called at the *end* of recording, but writes to the
        /// beginning of the file.
        /// </summary>
        void WriteMetadata()
        {
            if (m_Writer == null)
            {
                // Already closed
                return;
            }

            var metaProto = m_MetaData.ToProto();
            var metaProtoBytes = metaProto.ToByteArray();
            m_Writer.Write(metaProtoBytes, 0, metaProtoBytes.Length);
            m_Writer.Seek(0, 0);
            metaProto.WriteDelimitedTo(m_Writer);
        }

        /// <summary>
        /// Writes brain parameters to file.
        /// </summary>
        /// <param name="brainName">The name of the Brain the agent is attached to.</param>
        /// <param name="brainParameters">The parameters of the Brain the agent is attached to.</param>
        void WriteBrainParameters(string brainName, BrainParameters brainParameters)
        {
            if (m_Writer == null)
            {
                // Already closed
                return;
            }

            // Writes BrainParameters to file.
            m_Writer.Seek(MetaDataBytes + 1, 0);
            var brainProto = brainParameters.ToProto(brainName, false);
            brainProto.WriteDelimitedTo(m_Writer);
        }

        /// <summary>
        /// Write AgentInfo experience to file.
        /// </summary>
        /// <param name="info"> <see cref="AgentInfo"/> for the agent being recorded.</param>
        /// <param name="sensors">List of sensors to record for the agent.</param>
        internal void Record(AgentInfo info, List<ISensor> sensors)
        {
            if (m_Writer == null)
            {
                // Already closed
                return;
            }

            // --- STRICT ONE EPISODE ENFORCEMENT ---
            // If we have already completed 1 episode (numberEpisodes >= 1), 
            // we actively ignore any subsequent steps (like the start of the next episode)
            // until this writer is fully closed and a new one is created.
            if (m_MetaData.numberEpisodes >= 1)
            {
                return;
            }
            // --------------------------------------

            // Increment meta-data counters.
            m_MetaData.numberSteps++;
            m_CumulativeReward += info.reward;
            
            // If this step finishes the episode, EndEpisode() increments numberEpisodes to 1.
            // The NEXT call to Record() will be caught by the check above.
            if (info.done)
            {
                EndEpisode();
            }

            // Generate observations and add AgentInfo to file.
            var agentProto = info.ToInfoActionPairProto();
            foreach (var sensor in sensors)
            {
                agentProto.AgentInfo.Observations.Add(sensor.GetObservationProto(m_ObservationWriter));
            }

            agentProto.WriteDelimitedTo(m_Writer);
        }


        /// <summary>
        /// Performs all clean-up necessary.
        /// </summary>
        public void Close()
        {
            if (m_Writer == null)
            {
                // Already closed
                return;
            }

            // FIX: Only increment episode count if we haven't finished one yet.
            // If the agent finished naturally, numberEpisodes is already 1.
            // Calling EndEpisode() again causes the episode count to jump to 2, halving the mean reward.
            if (m_MetaData.numberEpisodes == 0)
            {
                EndEpisode();
            }
            
            // Avoid division by zero if closed immediately
            if (m_MetaData.numberEpisodes > 0)
            {
                m_MetaData.meanReward = m_CumulativeReward / m_MetaData.numberEpisodes;
            }
            else
            {
                m_MetaData.meanReward = 0;
            }
            
            WriteMetadata();
            m_Writer.Close();
            m_Writer = null;
        }

        /// <summary>
        /// Performs necessary episode-completion steps.
        /// </summary>
        void EndEpisode()
        {
            // Only increment if we haven't already (prevent double count on Close() if called after done)
            // But standard ML-Agents logic calls this often, so we rely on the Record check to gate it.
             m_MetaData.numberEpisodes += 1;
        }
    }
}