Dev NOTES:
- Token Reference ID must be unique when set. Using semantic ID with atomic guards due to Prune problems and reference documents
  update: not atomic guards usage due to not being removed automatically by delete by query, optimistic concurrency, relying on library,
  loading before store, worst case scenario a new document with same reference if will overwrite the existing one
- Delete by query does not support map-reduce indexes. We need to output the authorizations index to a collection in order 
  to run the query to prune tokens. see https://github.com/ravendb/ravendb/discussions/14496  


# OpenIDDict on RavenDB

Implementation of the OpenIdDict stores for the RavenDB database.

## Getting Started

### NuGet Package

## Usage

### RavenDB Static Indexes

### Simple usage

### Classes


### Unique values

Unique values are stored as separate documents with atomic guards. In cases where unique values are handled
the transaction mode will be set to cluster-wide as that is the requirement for atomic guards.

### ID generation

By default, set to `null` which implies HiLo identifier generation.  
Refer to official [RavenDB document](https://ravendb.net/docs/article-page/5.2/working-with-document-identifiers/client-api/document-identifiers/working-with-document-identifiers) about identifier generation strategies.  
When token reference id is not null, that reference id becomes part of a semantic id.


### Multi-tenant guidelines



## Release History


## Meta

Nikola Josipović

This project is licensed under the MIT License. See [License.md](License.md) for more information.

## Do you like this library?

<img src="https://img.shields.io/badge/%E2%82%B3%20%2F%20ADA-Buy%20me%20a%20coffee%20or%20two%20%3A)-green" alt="₳ ADA | Buy me a coffee or two :)" /> <br /><small> addr1q87dhpq4wkm5gucymxkwcatu2et5enl9z8dal4c0fj98fxznraxyxtx5lf597gunnxn3tewwr6x2y588ttdkdlgaz79spp3avz </small><br />

<img src="https://img.shields.io/badge/%CE%9E%20%2F%20ETH-...a%20nice%20cold%20beer%20%3A)-yellowgreen" alt="Ξ ETH | ...a nice cold beer :)" /> <br /> <small> 0xae0B28c1fCb707e1908706aAd65156b61aC6Ff0A </small><br />

<img src="https://img.shields.io/badge/%E0%B8%BF%20%2F%20BTC-...or%20maybe%20a%20good%20read%20%3A)-yellow" alt="฿ BTC | ...or maybe a good read :)" /> <br /> <small> bc1q3s8qjx59f4wu7tvz7qj9qx8w6ktcje5ktseq68 </small><br />

<img src="https://img.shields.io/badge/ADA%20POOL-Happy if you %20stake%20%E2%82%B3%20with%20Pale%20Blue%20Dot%20%5BPBD%5D%20%3A)-8a8a8a" alt="Happy if you stake ADA with Pale Blue Dot [PBD]" /> <br /> <small> <a href="https://palebluedotpool.org">https://palebluedotpool.org</a> </small>  