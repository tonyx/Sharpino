
## give the versiona as first parameter and storagename as second parameter and test/json as third parameter
## remember that the third parameter is highly dependent on the config as (test/json) (appSettings.json)

cat AggregateTemplate.sql | sed -e "s/{Version}/$1/g" | sed -e "s/{AggregateStorageName}/$2/g" |> "s/{Format}/$3/g"
