﻿#region legal notice
// Copyright(c) 2016 - 2018 Thermo Fisher Scientific - LSMS
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
#endregion legal notice
using System;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

using Thermo.Interfaces.ExactiveAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using IMsScan = Thermo.Interfaces.InstrumentAccess_V2.MsScanContainer.IMsScan;

namespace MetaDrive
{
	class CustomScansTandemByArrival
	{
		IScans m_scans = null;

		internal CustomScansTandemByArrival(Parameters parameters)
        {
            Parameters = parameters;
        }
        Parameters Parameters { get; set; }

        internal void DoJob(int time)
		{
			using (IExactiveInstrumentAccess instrument = Connection.GetFirstInstrument())
			{
				using (m_scans = instrument.Control.GetScans(false))
				{
                    m_scans.CanAcceptNextCustomScan += Scans_CanAcceptNextCustomScan;
                    IMsScanContainer orbitrap = instrument.GetMsScanContainer(0);
					Console.WriteLine("Waiting for scans on detector " + orbitrap.DetectorClass + "...");

					orbitrap.MsScanArrived += Orbitrap_MsScanArrived;
					Thread.CurrentThread.Join(time);
					orbitrap.MsScanArrived -= Orbitrap_MsScanArrived;
                    m_scans.CanAcceptNextCustomScan -= Scans_CanAcceptNextCustomScan;
                }
			}
		}

        private void Scans_CanAcceptNextCustomScan(object sender, EventArgs e)
        {
            // This event will be thrown on END of a CUSTOM scan, but non anytime when possible. Consider an instrument disconnect or an instrument reboot.
            if (true)
            {
                Console.WriteLine("CanAcceptNextCustomScan");
            }
        }

        private void Orbitrap_MsScanArrived(object sender, MsScanEventArgs e)
		{
			string accessId;
			using (IMsScan scan = (IMsScan) e.GetScan())	// caution! You must dispose this, or you block shared memory!
			{
				// The access ID gives a feedback about placed scans or scans generated by the instrument.
				scan.SpecificInformation.TryGetValue("Access Id:", out accessId);
				Console.WriteLine("{0:HH:mm:ss,fff} scan {1} arrived", DateTime.Now, accessId);

                ////// The common part is shared by all Thermo Fisher instruments, these settings mainly form the so called filter string
                ////// which also appears on top of each spectrum in many visualizers.
                //Console.WriteLine("----------------Common--------------");
                //Dump("Common", scan.CommonInformation);

                ////// The specific part is individual for each instrument type. Many values are shared by different Exactive Series models.
                //Console.WriteLine("----------------Specific--------------");
                // Dump("Specific", scan.SpecificInformation);

                //Dump(scan);

                var ib = IsBoxCarScan(scan);
                Console.WriteLine("IsBoxCar Scan: {0}", ib);

                FullScan.PlaceFullScan(m_scans, Parameters);

                //List<double> dynamicBoxCarRange = new List<double> { 600, 700, 800, 900, 1000 };
                //BoxCarScan.PlaceBoxCarScan(m_scans, Parameters, dynamicBoxCarRange);

                DataDependentScan.PlaceMS2Scan(m_scans, Parameters, 750);

                BoxCarScan.PlaceStaticBoxCarScan(m_scans, Parameters);
            }
		}

        private void Dump(string title, IInfoContainer container)
        {
            Console.WriteLine(title);
            foreach (string key in container.Names)
            {
                string value;
                // the source has to be "Unknown" to match all sources. Otherwise, the source has to match.
                // Sources can address values appearing in Tune files, general settings, but also values from
                // status log or additional values to a scan.
                MsScanInformationSource source = MsScanInformationSource.Unknown;   // show everything
                try
                {
                    if (container.TryGetValue(key, out value, ref source))
                    {
                        string descr = source.ToString();
                        descr = descr.Substring(0, Math.Min(11, descr.Length));
                        Console.WriteLine("   {0,-11} {1,-35} = {2}", descr, key, value);
                    }
                }
                catch { /* up to and including 2.8SP1 there is a bug displaying items which are null and if Foundation 3.1SP4 is used with CommonCore */ }
            }
            
        }

        private void Dump(IMsScan scan)
        {
            Console.WriteLine("----------------------------");
            object massRanges;
            ThermoFisher.Foundation.IO.Range[] x = new ThermoFisher.Foundation.IO.Range[] { };
            try
            {
                if (scan.CommonInformation.TryGetRawValue("MassRanges", out massRanges))
                {
                    Console.WriteLine("MassRanges:");
                    x = (ThermoFisher.Foundation.IO.Range[])massRanges;
                    Console.WriteLine("     {0}, {1}", x.First().Low, x.First().High);

                    
                }
            }
            catch { }

            object a;

            try
            {
                if (scan.CommonInformation.TryGetRawValue("SourceFragmentationInfo", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("SourceFragmentationInfo     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("MultipleActivations", out a))
                {
                    var b = (Boolean[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("MultipleActivations     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("Activations", out a))
                {
                    var b = (ThermoFisher.Foundation.IO.ScanEventBase.ActivationType[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("Activations     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("EnergiesValid", out a))
                {
                    var b = (ThermoFisher.Foundation.IO.ScanEventBase.EnergyType[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("EnergiesValid     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("IsolationWidthOffset", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("IsolationWidthOffset     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("LastPrecursorMasses", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("LastPrecursorMasses     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("FirstPrecursorMasses", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("FirstPrecursorMasses     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("PrecursorRangeValidities", out a))
                {
                    var b = (Boolean[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("PrecursorRangeValidities     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("Energies", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("Energies     {0},", ib);
                    }
                }
            }
            catch { }

            try
            {
                if (scan.CommonInformation.TryGetRawValue("Masses", out a))
                {
                    var b = (double[])a;
                    foreach (var ib in b)
                    {
                        Console.WriteLine("Masses     {0},", ib);
                    }
                }
            }
            catch { }

            Console.WriteLine("----------------------------");
        }

        private bool IsBoxCarScan(IMsScan scan)
        {

            string value;
            string valueHigh;
            if (scan.CommonInformation.TryGetValue("LowMass", out value) && scan.CommonInformation.TryGetValue("HighMass", out valueHigh))
            {
                Console.WriteLine("Scan LowMass: " + value );
            }

            object massRanges;
            ThermoFisher.Foundation.IO.Range[] x = new ThermoFisher.Foundation.IO.Range[] { };
            if (scan.CommonInformation.TryGetRawValue("MassRanges", out massRanges))
            {
                x = (ThermoFisher.Foundation.IO.Range[])massRanges;
                Console.WriteLine("MassRanges:  {0}, {1}", x.First().Low, x.First().High);

                Console.WriteLine("Mass Ranges count: {0}", x.Count());
            }

            string msorder;
            if (scan.CommonInformation.TryGetValue("MSOrder", out msorder))
            {
                Console.WriteLine("Mass Order: " + msorder);
            }


            string massRangeCount;

            if (scan.CommonInformation.TryGetValue("MassRangeCount", out massRangeCount))
            {
                Console.WriteLine("BoxCar Scan Boxes: {0}.", int.Parse(massRangeCount));

                if (int.Parse(massRangeCount) > 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
