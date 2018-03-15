using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace SF_HKDev.StatefulAPI.Controllers
{
    [Route("api/[controller]")]
    public class ValuesController : Controller
    {
        private readonly IReliableStateManager _stateManager;

        public ValuesController(IReliableStateManager stateManager)
        {
            _stateManager = stateManager;
        }
        // GET api/values
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            CancellationToken ct = new CancellationToken();

            IReliableDictionary<string, string> votesDictionary = await _stateManager.GetOrAddAsync<IReliableDictionary<string, string>>("values");

            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                Microsoft.ServiceFabric.Data.IAsyncEnumerable<KeyValuePair<string, string>> list = await votesDictionary.CreateEnumerableAsync(tx);

                Microsoft.ServiceFabric.Data.IAsyncEnumerator<KeyValuePair<string, string>> enumerator = list.GetAsyncEnumerator();

                List<string> result = new List<string>();

                while (await enumerator.MoveNextAsync(ct))
                {
                    result.Add(enumerator.Current.Value);
                }

                return Json(result);
            }
        }

        // POST api/values
        [HttpPost]
        public async Task<IActionResult> Post(string value)
        {
            IReliableDictionary<string, string> valuesList = await _stateManager.GetOrAddAsync<IReliableDictionary<string, string>>("values");

            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                await valuesList.AddAsync(tx, value, value);
                await tx.CommitAsync();
            }

            return new OkResult();
        }

    }
}
