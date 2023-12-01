KAFKADIR="$HOME/sandbox/kafka/kafka_2.13-3.4.0"

$KAFKADIR/bin/kafka-topics.sh --create --topic categories-02 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic tags-01 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic todo-01 --bootstrap-server localhost:9092
$KAFKADIR/bin/kafka-topics.sh --create --topic todo-02 --bootstrap-server localhost:9092