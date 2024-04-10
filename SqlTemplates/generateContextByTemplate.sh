echo $1

## give the versiona as first parameter and storagename as second parameter

cat ContextTemplate.sql | sed -e "s/{Version}/$1/g" | sed -e "s/{ContextStorageName}/$2/g"
