
## give the version as first parameter and storagename as second parameter and text/json as third parameter
## remember that the third parameter depends on the config as (text/json) (appSettings.json)

cat AggregateTemplate.sql | sed -e "s/{Version}/$1/g" | sed -e "s/{AggregateStorageName}/$2/g" | sed -e "s/{Format}/$3/g"
