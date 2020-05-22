﻿using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    public class HaswellClientArb : HaswellClientUncore
    {
        private ulong lastUncoreClockCount;

        public HaswellClientArb()
        {
            architectureName = "Haswell Client System Agent";
            lastUncoreClockCount = 0;
            monitoringConfigs = new MonitoringConfig[1];
            monitoringConfigs[0] = new AllRequests(this);
        }

        public class NormalizedArbCounterData
        {
            public float uncoreClock;
            public float ctr0;
            public float ctr1;
        }

        public NormalizedArbCounterData UpdateArbCounterData()
        {
            NormalizedArbCounterData rc = new NormalizedArbCounterData();
            float normalizationFactor = GetNormalizationFactor(0);
            ulong uncoreClock, elapsedUncoreClocks;
            ulong ctr0 = ReadAndClearMsr(MSR_UNC_ARB_PERFCTR0);
            ulong ctr1 = ReadAndClearMsr(MSR_UNC_ARB_PERFCTR1);
            Ring0.ReadMsr(MSR_UNC_PERF_FIXED_CTR, out uncoreClock);

            // MSR_UNC_PERF_FIXED_CTR is 48 bits wide, upper bits are reserved
            uncoreClock &= 0xFFFFFFFFFFFF;
            elapsedUncoreClocks = uncoreClock;
            if (uncoreClock > lastUncoreClockCount)
                elapsedUncoreClocks = uncoreClock - lastUncoreClockCount;
            lastUncoreClockCount = uncoreClock;

            rc.ctr0 = ctr0 * normalizationFactor;
            rc.ctr1 = ctr1 * normalizationFactor;
            rc.uncoreClock = elapsedUncoreClocks * normalizationFactor;
            return rc;
        }

        public class AllRequests : MonitoringConfig
        {
            private HaswellClientArb cpu;
            public string GetConfigName() { return "All MC Requests"; }

            public AllRequests(HaswellClientArb intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnableUncoreCounters();
                // 0x80 = increments by number of outstanding requests every cycle
                // counts for coherent and non-coherent requests initiated by cores, igpu, or L3
                // only works in counter 0
                Ring0.WriteMsr(MSR_UNC_ARB_PERFEVTSEL0,
                    GetUncorePerfEvtSelRegisterValue(0x80, 1, false, false, true, false, 0));

                // 0x81 = number of requests
                Ring0.WriteMsr(MSR_UNC_ARB_PERFEVTSEL1,
                    GetUncorePerfEvtSelRegisterValue(0x81, 1, false, false, true, false, 0));
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = null;
                NormalizedArbCounterData counterData = cpu.UpdateArbCounterData();

                results.overallMetrics = new string[] { FormatLargeNumber(counterData.uncoreClock),
                    FormatLargeNumber(counterData.ctr1),
                    string.Format("{0:F2}", counterData.ctr0 / counterData.uncoreClock),
                    string.Format("{0:F2} clk", counterData.ctr0 / counterData.ctr1)
                };
                return results;
            }

            public string GetHelpText()
            {
                return "Clk - uncore active clocks\n" +
                    "Requests - all requests to the system agent\n" +
                    "Q Occupancy - average arbitration queue occupancy, when a request is pending\n" +
                    "Req Latency - average time each request stayed in the arbitration queue";
            }

            public string[] columns = new string[] { "Clk", "Requests", "Q Occupancy", "Req Latency" };
        }
    }
}
