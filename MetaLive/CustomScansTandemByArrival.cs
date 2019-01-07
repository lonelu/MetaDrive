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

using Thermo.Interfaces.ExactiveAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using IMsScan = Thermo.Interfaces.InstrumentAccess_V2.MsScanContainer.IMsScan;

namespace MetaLive
{
	/// <summary>
	/// Place 10 individual scans after arrival of at least one scan.
	/// Observe the scan IDs at different acquisition speed settings by chosing small various resolutions.
	/// </summary>
	class CustomScansTandemByArrival
	{
		int m_scanId = 1;   // must be != 0
		IScans m_scans = null;

		internal CustomScansTandemByArrival() { }

		internal void DoJob()
		{
			using (IExactiveInstrumentAccess instrument = Connection.GetFirstInstrument())
			{
				using (m_scans = instrument.Control.GetScans(false))
				{
					IMsScanContainer orbitrap = instrument.GetMsScanContainer(0);
					Console.WriteLine("Waiting 60 seconds for scans on detector " + orbitrap.DetectorClass + "...");

					orbitrap.MsScanArrived += Orbitrap_MsScanArrived;
					Thread.CurrentThread.Join(20000);
					orbitrap.MsScanArrived -= Orbitrap_MsScanArrived;
				}
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
				PlaceScan();
			}
		}

		private void PlaceScan()
		{
			// If no information about possible settings are available yet or if we finished our job, we bail out.
			if ((m_scanId > 10) || (m_scans.PossibleParameters.Length == 0))
			{
				return;
			}
			ICustomScan scan = m_scans.CreateCustomScan();
			scan.RunningNumber = m_scanId++;
			scan.Values["Polarity"] = "1";
			Console.WriteLine("{0:HH:mm:ss,fff} placing scan {1}", DateTime.Now, scan.RunningNumber);
			m_scans.SetCustomScan(scan);
		}
	}
}
