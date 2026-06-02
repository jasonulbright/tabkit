# Example pipelines

Three working YAML pipeline definitions for `tabkit extract`. Each is fully runnable once the source data exists at the referenced path.

| File | What it does | Audience |
|---|---|---|
| [01-orders-csv-to-hyper.yml](01-orders-csv-to-hyper.yml) | CSV → cast types → filter → `.hyper` | Smallest viable Prep Builder replacement. |
| [02-mssql-incremental-to-hyper.yml](02-mssql-incremental-to-hyper.yml) | MSSQL last-13-months slice → `.hyper` | Nightly refresh pattern, env-var creds. |
| [03-parquet-warehouse-fanout.yml](03-parquet-warehouse-fanout.yml) | Parquet → strip metadata → `.hyper` | Data lake → BI handoff without Prep Conductor. |

## Quick start

The first pipeline runs end-to-end if you create a sample CSV:

```bash
mkdir -p data out
cat > data/orders.csv <<'EOF'
order_id,total,order_date
1,49.99,2024-03-15
2,0.00,2024-03-16
3,129.50,2024-04-01
EOF

tabkit extract run examples/pipelines/01-orders-csv-to-hyper.yml
# → rows in: 3 / rows out: 2 / out/orders.hyper written
```

Open `out/orders.hyper` in Tableau Desktop or query it via the Hyper API to verify.
