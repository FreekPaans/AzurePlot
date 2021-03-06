﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using AzurePlot.Web.Setup;

namespace AzurePlot.Web.DataAccess {
	public class AzureSubscription {
		[Key]
		public int Id{get;set;}
		[MaxLength]
		public string Name { get; set; }
		[MaxLength]
		public string FromSessionId { get; set; }
		[MaxLength]
		public string AzureSubscriptionId { get; set; }

		public DateTime AddedOnUtc { get; set; }
	
		[MaxLength]
		public byte[] Pfx { get; set; }
		[MaxLength]
		public string Password { get; set; }

		public string FormatName() {
			if(string.IsNullOrWhiteSpace(Name)) {
				return "<Unknown>";
			}
			return Name;
		}


		internal Lib.MetricsEndpointConfiguration GetMetricsConfig() {
			return new FromPKCSMetricsEndpointConfiguration(Pfx,Password,AzureSubscriptionId);
		}
	}
}
