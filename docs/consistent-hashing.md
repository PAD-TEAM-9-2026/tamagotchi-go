# Consistent hashing and the sharded cache

Research notes for the Lab 5 cache: why we shard with a consistent-hashing ring, what
the measurements show, and how our ring differs from Redis Cluster. The rules every
implementation follows are in [contracts/cache.md](../contracts/cache.md).

## The problem

The cache is split over several nodes (`cache-0`, `cache-1`, `cache-2`). Every service
replica must agree, without asking anyone, on which node holds a key. The rule must
also survive the node list changing: when a node is added or removed, as few keys as
possible should change node, because every key that moves is a cache miss and one
more read on the database.

## Modulo hashing

The obvious rule is `node = hash(key) % N`. It spreads keys evenly, but it ties every
key to `N`. Going from 3 to 4 nodes keeps a key in place only when
`hash % 3 == hash % 4`, which holds for 3 of every 12 values, so **about 75% of the
keys move**. Most of them move between the old nodes, not to the new one. After a
resize the cache is nearly empty and the database takes the full load.

## Consistent hashing

Consistent hashing (Karger et al., 1997) hashes both nodes and keys onto the same
circle of 2^64 positions. A key belongs to the first node point found walking
clockwise from the key's position, wrapping past the top.

- **Adding a node** places new points on the circle. Only the keys between a new
  point and the point before it change owner, and they all move to the new node.
  The expected share is `1/(N+1)`: 25% when going from 3 to 4 nodes.
- **Removing a node** hands only that node's keys to the next points clockwise.
  Every other key stays where it was.

### Virtual nodes

With one point per node the arcs between points are random, so one node can own half
the circle. Each node is therefore placed many times, at `hash("{name}#0")` to
`hash("{name}#{V-1}")`. Each node's share is then the sum of `V` random arcs, and its
relative deviation shrinks roughly as `1/sqrt(V)`: about 9% for 128 virtual nodes and
3% for 1024. Virtual nodes also spread a removed node's keys over all remaining
nodes instead of dumping them on one neighbour.

## Our implementation

| Choice | Value | Why |
|---|---|---|
| Hash | first 8 bytes of MD5, big-endian unsigned 64-bit | Built into Node, .NET and Python; only spreads keys, not used for security |
| Virtual nodes | 128 per node | Balance within about 10% with a ring of 384 points |
| Node identity | the name in `CACHE_NODES`, never the address | Moving a node to another host must not move keys |
| Ties | lower node name wins | Deterministic in every language |
| Where it runs | in each service, through `kits/typescript` and `kits/csharp` | No proxy hop, no extra container |

Two kits in two languages must place every key identically, otherwise two replicas
would read and write different nodes. [cache-ring-vectors.json](../contracts/cache-ring-vectors.json)
fixes 200 keys and their expected node for 3 nodes, `cache-1` removed and `cache-3`
added. The Python reference ([tools/cache_ring.py](../tools/cache_ring.py)), the
TypeScript kit and the C# kit all pass every vector.

### A node that is down is not a removed node

| Event | What happens |
|---|---|
| `cache-1` stops or times out (50 ms) | It is marked down for 10 s. Its keys are read from the database and **not moved**: the ring is unchanged, so they come back to `cache-1` when it answers again. Responses stay `200`. |
| `cache-1` is removed from `CACHE_NODES` and services restart | The ring changes. Only `cache-1`'s keys move, spread over `cache-0` and `cache-2`, and each misses once. |

## Experiment

[tools/cache_experiment.py](../tools/cache_experiment.py) places 100,000 keys shaped like
our real keys (`{service}:v1:{resource}:{uuid}`) with the same ring as the kits.
Reproduce with `python tools/cache_experiment.py`.

### Spread over 3 nodes

| Placement | cache-0 | cache-1 | cache-2 | max / mean | std dev / mean |
|---|---|---|---|---|---|
| ring, 1 virtual node | 20.40% | 48.32% | 31.28% | 1.450 | 34.47% |
| ring, 16 virtual nodes | 40.69% | 34.20% | 25.11% | 1.221 | 19.16% |
| **ring, 128 virtual nodes (ours)** | 38.00% | 32.83% | 29.17% | 1.140 | 10.87% |
| ring, 1024 virtual nodes | 33.76% | 32.27% | 33.98% | 1.019 | 2.28% |
| `hash % 3` | 33.47% | 33.21% | 33.32% | 1.004 | 0.31% |
| Redis Cluster slots | 33.30% | 33.48% | 33.22% | 1.004 | 0.32% |

### Keys moved when a 4th node joins

| Placement | Moved | Of keys | Ideal | Moved keys that went to the new node |
|---|---|---|---|---|
| ring, 1 virtual node | 14,689 | 14.69% | 25% | 100% |
| ring, 16 virtual nodes | 21,498 | 21.50% | 25% | 100% |
| **ring, 128 virtual nodes (ours)** | 21,659 | 21.66% | 25% | 100% |
| ring, 1024 virtual nodes | 25,880 | 25.88% | 25% | 100% |
| `hash % N`, 3 to 4 | 75,239 | 75.24% | 25% | 33% |
| Redis Cluster, 4,096 slots resharded | 25,232 | 25.23% | 25% | 100% |

Removing `cache-1` from our ring moves 32,830 keys (32.83%), exactly the keys it held.

### What the numbers say

- **Modulo balances perfectly and breaks on every resize.** It moves 75% of the keys,
  and two thirds of those only shuffle between the old nodes.
- **The ring moves only what it must.** Every moved key went to the new node, and
  removal moved only the removed node's keys. This is the property we rely on.
- **Virtual nodes buy balance.** One point per node gave a 20% / 48% split; 128 points
  bring the largest node to 1.14 times the mean, 1024 to 1.02.
- **With 128 virtual nodes the new node took 21.7%, not 25%.** Its 128 points happen to
  cover a slightly short arc. With 1024 the share is 25.9%. 128 is the contract value:
  three nodes on one machine do not need tighter balance, and changing it now would
  mean regenerating the vectors for both kits.

## Comparison with Redis Cluster

Redis Cluster (and Valkey in cluster mode) also shards keys, but with a different
algorithm and a different division of work between client and server.

| | Our ring | Redis Cluster |
|---|---|---|
| Key to position | `MD5(key)`, first 8 bytes, a 2^64 circle | `CRC16(key) mod 16384`, one of 16,384 hash slots |
| Position to node | clockwise to the next of 128 hashed points per node | an explicit table: each master owns ranges of slots |
| Who decides | every client computes it from `CACHE_NODES` | the cluster owns the slot table; clients cache a copy |
| Wrong node | cannot happen if clients share the same node list (the vectors guarantee the same algorithm) | the server answers `MOVED` (slot owned elsewhere) or `ASK` (slot being migrated) and the client retries there |
| Balance | statistical, about 10% spread with 128 virtual nodes | exact: slots are assigned in equal counts (33.3% each in our experiment) |
| Adding a node | config change and restart; about `1/N` of keys now map to the new node and miss once | `redis-cli --cluster reshard` moves slots one by one with `MIGRATE`; keys are copied, nothing is lost or missed |
| Removing a node | its keys map to the remaining nodes and miss once | its slots must first be moved to other masters |
| Node failure | its keys miss and are read from the database for 10 s windows; no error | a replica of that master is promoted by the other masters; without a replica its slots are uncovered and, by default (`cluster-require-full-coverage yes`), the whole cluster stops accepting queries |
| Replication | none; the cache is disposable | replicas per master, built in |
| Multi-key operations | not supported across nodes; we do not use them | allowed when all keys share a slot; `{hash tags}` force it (`{user:42}:profile` and `{user:42}:wallet` hash only `user:42`), otherwise `CROSSSLOT` |
| Membership | static list in each service's environment | gossip on the cluster bus (port + 10000), failure detection by majority of masters |
| Client | any Redis/Valkey client in standalone mode | a cluster-aware client that follows redirects and refreshes the slot map |
| Minimum deployment | 1 node works, we run 3 | 3 masters, usually with 3 replicas |

Hash slots are themselves a form of consistent hashing: 16,384 fixed "virtual nodes"
whose owners are assigned explicitly instead of by hashing node names. Resharding
moves whole slots, so like our ring it moves only about `1/N` of the keys (25.2% in
the experiment), and unlike `hash % N` it never reshuffles the rest.

The difference is where the work happens. Redis Cluster keeps the data during
changes: it migrates keys between servers and redirects clients while it does. Our
ring keeps the servers simple and lets the moved keys miss, which is acceptable only
because a cache can always be rebuilt from the database. For a primary datastore the
cluster's migration and replication would be required.

**Why we built the ring:** the lab asks us to implement sharding based on consistent
hashing, our cached data is disposable, plain standalone Valkey needs no cluster setup
or cluster-aware clients, and a stopped node degrades to database reads instead of
errors.

## Other algorithms

| Algorithm | Idea | Trade-off |
|---|---|---|
| Rendezvous (highest random weight) | score `hash(key, node)` for every node, pick the highest | perfect minimal movement, no ring, but `O(N)` per lookup |
| Jump consistent hash | maps a key to a bucket `0..N-1` with a short loop | no memory and near-perfect balance, but nodes can only be added or removed at the end |
| Maglev | a fixed lookup table filled from node preferences | very fast lookups and good balance, a small extra movement on change |

## Quick answers

- **3 to 4 nodes with modulo?** About 75% of keys move; measured 75.24%.
- **With our ring?** Only the new node's share, about 25%; measured 21.66%, all to the new node.
- **Why virtual nodes?** Balance: 1 point gave 20% / 48% / 31%; 128 points give 38% / 33% / 29%.
- **A node stops?** Its keys miss and come from the database; nothing moves; still `200`.
- **A node is removed from the config?** The ring changes and only its keys move.
- **Redis Cluster?** 16,384 CRC16 slots assigned to masters, `MOVED`/`ASK` redirects, slot migration, replicas and hash tags; ours is client-side, replica-free and lets moved keys miss.

## References

- D. Karger et al., "Consistent Hashing and Random Trees", STOC 1997.
- Redis Cluster specification: https://redis.io/docs/latest/operate/oss_and_stack/reference/cluster-spec/
- Valkey cluster tutorial: https://valkey.io/topics/cluster-tutorial/
- J. Lamping, E. Veach, "A Fast, Minimal Memory, Consistent Hash Algorithm", 2014.
- D. Thaler, C. Ravishankar, "Using Name-Based Mappings to Increase Hit Rates", IEEE/ACM ToN 1998.
