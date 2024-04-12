echo $1

## give the versiona as first parameter and storagename as second parameter

cat AggregateTemplate.sql | sed -e "s/{Version}/$1/g" | sed -e "s/{AggregateStorageName}/$2/g"
