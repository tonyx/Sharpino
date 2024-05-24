
## give the versiona as first parameter and storagename as second parameter and test/json as third parameter
## remember that the third parameter is highly dependent on the config as (test/json) (appSettings.json)

cat ContextTemplate.sql | sed -e "s/{Version}/$1/g" | sed -e "s/{ContextStorageName}/$2/g" | sed -e "s/{Format}/$3/g"
