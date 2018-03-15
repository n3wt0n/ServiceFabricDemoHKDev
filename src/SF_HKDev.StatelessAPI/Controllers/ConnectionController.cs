using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SF_HKDev.StatelessAPI.Controllers
{
    [Route("api/[controller]")]
    public class ConnectionController : Controller
    {
        // GET api/connection
        [HttpGet]
        public bool Get()
        {
            return true;
        }
    }
}
