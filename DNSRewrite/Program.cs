using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Interfaces;
using MultipleHttpsRequest;
using MultiplePing;

namespace DNSRewrite
{
    public class Program
    {
        static void Main(string[] args)
        {
            string[] domainLines = new string[0];
            string[] dnsServerLines = new string[0];

            List<LookupClient> dnsClients = new List<LookupClient>();
            Dictionary<string, List<IPAddress>> domains = new Dictionary<string, List<IPAddress>>();

            // Load domain list and DNS server list
            try
            {
                domainLines = File.ReadAllLines("domains.txt");
                foreach (string domainLine in domainLines)
                {
                    domains[domainLine] = new List<IPAddress>();
                }

                dnsServerLines = File.ReadAllLines("server.txt");
                foreach (string dnsServerLine in dnsServerLines)
                {
                    if (!dnsServerLine.StartsWith("//"))
                    {
                        string dnsServer = dnsServerLine.Split(',')[0].Split(':')[0];
                        int dnsServerPort = Convert.ToInt32(dnsServerLine.Split(',')[0].Split(':')[1]);
                        bool dnsServerViaTcp = Convert.ToBoolean(dnsServerLine.Split(',')[1]);

                        dnsClients.Add(new LookupClient(new IPEndPoint(IPAddress.Parse(dnsServer), dnsServerPort))
                        {
                            UseTcpOnly = dnsServerViaTcp
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();

                Console.WriteLine("Usage:");
                Console.WriteLine("domains.txt contains the domains to query, one line per domain");
                Console.WriteLine();
                Console.WriteLine("server.txt contains the DNS server(s) to query, format is \"IP:PORT,TCP ONLY\", e.g. \"8.8.8.8:53,false\", one line per server");
                Console.WriteLine("// comments the line");
                Console.WriteLine();

                Console.WriteLine("Result:");
                Console.WriteLine("When there's only one DNS server added, the result will directly be output");
                Console.WriteLine("When multiple DNS servers are added, the resolved IPs are pinged to choose the fastest one, or the first one if all fails ping");
                Console.WriteLine("The output is in dnsmasq format, and you can copy it to your dnsmasq settings");

                return;
            }

            // Query all DNS servers for the domains listed
            foreach (LookupClient client in dnsClients)
            {
                Console.WriteLine($"DNS server is {client.NameServers.FirstOrDefault().Endpoint}, queries done below will be based on this");
                foreach (string domain in domainLines)
                {
                    try
                    {
                        var result = client.Query(domain, QueryType.A).Answers.ARecords().Select(x => x.Address);
                        Console.WriteLine($"Query {domain} = {string.Join(", ", result)}");
                        domains[domain].AddRange(result);
                    }
                    catch (DnsResponseException)
                    {
                        Console.WriteLine($"Query {domain} = **FAILED**");
                    }
                }
                Console.WriteLine();
            }
            Console.WriteLine();

            // Get the final results
            QueryResultCollection finalResults = new QueryResultCollection();

            // If only one DNS server specified, just pick the first query result from resovled IPs
            if (dnsClients.Count == 1)
            {
                foreach (string domain in domains.Keys)
                {
                    finalResults.Add(new QueryResultCollection.QueryResult(domain, domains[domain].First().ToString()));
                }
            }
            else
            {
                // Ping the IPs
                Console.WriteLine("Ping all queried IPs");
                List<string> totalIPs = domains.SelectMany(x => x.Value).Select(x => x.ToString()).Distinct().ToList();

                ConcurrentDictionary<string, IRequestReply> pingResults = GetResponses(totalIPs, GetResponseType.Ping);
                ConcurrentDictionary<string, IRequestReply> httpsResults = GetResponses(pingResults.Values.Where(x => x.SuccessRate == 0).Select(x => x.IP), GetResponseType.Http);

                Dictionary<string, IRequestReply> results = new Dictionary<string, IRequestReply>();
                foreach (string ip in pingResults.Keys)
                {
                    if (pingResults[ip].SuccessRate > 0)
                    {
                        results.Add(ip, pingResults[ip]);
                    }
                    else
                    {
                        results.Add(ip, httpsResults[ip]);
                    }
                }

                Console.WriteLine();

                // Group the IPs by domain
                Console.WriteLine("Query results per domain as group");
                foreach (string domain in domains.Keys)
                {
                    Console.WriteLine($">> {domain}");

                    var resultsOfDomain = domains[domain].Select(x => results[x.ToString()]).Distinct();

                    Console.WriteLine(string.Join(Environment.NewLine, resultsOfDomain));

                    // All fail, means the domains bans ping - pick the first one
                    if (resultsOfDomain.All(x => x.SuccessRate == 0))
                    {
                        finalResults.Add(new QueryResultCollection.QueryResult(domain, resultsOfDomain.First().IP));
                    }

                    // Else, pick the lowest RTT, but success rate > 0
                    else
                    {
                        IRequestReply lowestRttReply = resultsOfDomain.First(x => x.SuccessRate > 0);
                        double lowestRtt = lowestRttReply.AverageRtt;

                        foreach (IRequestReply replyCollection in resultsOfDomain.Where(x => x.SuccessRate > 0))
                        {
                            if (replyCollection.AverageRtt < lowestRtt)
                            {
                                lowestRttReply = replyCollection;
                                lowestRtt = replyCollection.AverageRtt;
                            }
                        }

                        finalResults.Add(new QueryResultCollection.QueryResult(domain, lowestRttReply.IP));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Query results in the best combination");
            Console.WriteLine(string.Join(Environment.NewLine, finalResults));
        }


        public enum GetResponseType
        {
            Ping,
            Http
        }

        private static ConcurrentDictionary<string, IRequestReply> GetResponses(IEnumerable<string> totalIPs, GetResponseType getResponseType)
        {
            ConcurrentDictionary<string, IRequestReply> pingResults = new ConcurrentDictionary<string, IRequestReply>();
            List<Task> pingTasks = new List<Task>();
            foreach (string ip in totalIPs)
            {
                pingTasks.Add(Task.Run(() =>
                {
                    IRequestReply replies = null;

                    switch (getResponseType)
                    {
                        case GetResponseType.Ping:
                            replies = PingReplyMultiple.Run(ip);
                            break;

                        case GetResponseType.Http:
                            replies = HttpsReplyMultiple.Run(ip);
                            break;
                    }

                    pingResults.AddOrUpdate(ip, replies, (ip, value) => value);
                    Console.WriteLine(replies);
                }));
            }
            var pingTaskArray = pingTasks.ToArray();
            Task.WaitAll(pingTaskArray);
            return pingResults;
        }

        public class QueryResultCollection : IEnumerable<QueryResultCollection.QueryResult>
        {
            public List<QueryResult> results = new List<QueryResult>();

            public void Add(QueryResult queryResult) => results.Add(queryResult);

            public IEnumerator<QueryResult> GetEnumerator()
            {
                return ((IEnumerable<QueryResult>)results).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<QueryResult>)results).GetEnumerator();
            }

            public class QueryResult
            {
                public string Domain { get; set; }
                public string IP { get; set; }

                public QueryResult(string domain, string ip)
                {
                    Domain = domain;
                    IP = ip;
                }

                public override string ToString() => $"address=/{Domain}/{IP}";
            }
        }
    }
}
