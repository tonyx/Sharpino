
# just a sketch of the way to create topics in kafka. Consult your docs and adapt to your needs.
# remember the relation between the topic name and the version + storagename of the context or aggregate.

KAFKADIR="$HOME/sandbox/kafka/kafka_2.13-3.4.0"

$KAFKADIR/bin/kafka-topics.sh --create --topic categories-02 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic tags-01 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic todo-01 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic todo-02 --bootstrap-server localhost:9092

######
docker-compose exec kafka kafka-topics.sh --create --topic categories-02 --bootstrap-server localhost:9092
docker-compose exec kafka kafka-topics.sh --create --topic tags-01 --bootstrap-server localhost:9092
docker-compose exec kafka kafka-topics.sh --create --topic todo-01 --bootstrap-server localhost:9092
docker-compose exec kafka kafka-topics.sh --create --topic todo-02 --bootstrap-server localhost:9092


