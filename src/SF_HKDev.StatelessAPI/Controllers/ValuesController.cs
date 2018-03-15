using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SF_HKDev.StatelessAPI.Controllers
{
    [Route("api/[controller]")]
    public class ValuesController : Controller
    {
        private readonly StatelessServiceContext _context;

        public ValuesController(StatelessServiceContext context)
        {
            _context = context;
        }

        // GET api/values
        [HttpGet]
        public string Get()
            => _context.NodeContext.NodeName;
        
    }
}
