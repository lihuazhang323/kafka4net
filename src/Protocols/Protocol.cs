﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using kafka4net.Metadata;
using kafka4net.Protocols.Requests;
using kafka4net.Protocols.Responses;
using kafka4net.Utils;

namespace kafka4net.Protocols
{
    internal class Protocol
    {
        private static readonly ILogger _log = Logger.GetLogger();

        private readonly Cluster _cluster;

        internal Protocol(Cluster cluster)
        {
            _cluster = cluster;
        }

        internal async Task<ProducerResponse> ProduceRaw(ProduceRequest request, CancellationToken cancel)
        {
            var conn = request.Broker.Conn;
            var client = await conn.GetClientAsync();
            var response = await conn.Correlation.SendAndCorrelateAsync(
                id => Serializer.Serialize(request, id),
                Serializer.GetProducerResponse,
                client, cancel);
            
            if(response.Topics.Any(t => t.Partitions.Any(p => p.ErrorCode != ErrorCode.NoError)))
                _log.Debug("_");

            return response;
        }

        internal async Task<ProducerResponse> Produce(ProduceRequest request)
        {
            var conn = request.Broker.Conn;
            var client = await conn.GetClientAsync();
            _log.Debug("Sending ProduceRequest to {0}, Request: {1}", conn, request);
            var response = await conn.Correlation.SendAndCorrelateAsync(
                id => Serializer.Serialize(request, id),
                Serializer.GetProducerResponse,
                client,
                CancellationToken.None
            );
            _log.Debug("Got ProduceResponse: {0}", response);

            return response;
        }

        internal async Task<MetadataResponse> MetadataRequest(TopicRequest request, BrokerMeta broker = null)
        {
            TcpClient tcp;
            Connection conn;

            if (broker != null)
            {
                conn = broker.Conn;
                tcp = await conn.GetClientAsync();
            }
            else
            {
                var clientAndConnection = await _cluster.GetAnyClientAsync();
                conn = clientAndConnection.Item1;
                tcp = clientAndConnection.Item2;
            }

            //var tcp = await (broker != null ? broker.Conn.GetClientAsync() : _cluster.GetAnyClientAsync());
            _log.Debug("Sending MetadataRequest to {0}", tcp.Client.RemoteEndPoint);

            return await conn.Correlation.SendAndCorrelateAsync(
                id => Serializer.Serialize(request, id),
                Serializer.DeserializeMetadataResponse,
                tcp, CancellationToken.None);
        }

        internal async Task<OffsetResponse> GetOffsets(OffsetRequest req, Connection conn)
        {
            var tcp = await conn.GetClientAsync();
            if(_log.IsDebugEnabled)
                _log.Debug("Sending OffsetRequest to {0}. request: {1}", tcp.Client.RemoteEndPoint, req);
            var response = await conn.Correlation.SendAndCorrelateAsync(
                id => Serializer.Serialize(req, id),
                Serializer.DeserializeOffsetResponse,
                tcp, CancellationToken.None);
            _log.Debug("Got OffsetResponse {0}", response);
            return response;
        }

        internal async Task<FetchResponse> Fetch(FetchRequest req, Connection conn)
        {
            _log.Debug("Sending FetchRequest to broker {1}. Request: {0}", req, conn);
            
            // Detect disconnected server. Wait no less than 5sec. 
            // If wait time exceed wait time + 3sec, consider it a timeout too
            var timeout = Math.Max(5000, req.MaxWaitTime + 3000);
            var cancel = new CancellationTokenSource(timeout);

            var tcp = await conn.GetClientAsync();
            var response = await conn.Correlation.SendAndCorrelateAsync(
                id => Serializer.Serialize(req, id),
                Serializer.DeserializeFetchResponse,
                tcp, cancel.Token);

            if(response.Topics.Length > 0 && _log.IsDebugEnabled)
                _log.Debug("Got fetch response from {0} Response: {1}", conn, response);

            return response;
        }
    }
}
