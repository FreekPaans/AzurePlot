﻿using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;

namespace WadGraphEs.MetricsEndpoint.DataAccess {
	public class DataContext : IdentityDbContext<ProxyUser>{
		public DataContext() {
			Database.SetInitializer<DataContext>(null);
		}
	}
}