package raven.client.document;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.zip.GZIPOutputStream;

import org.apache.commons.lang.ArrayUtils;
import org.apache.http.Header;
import org.apache.http.HttpEntity;
import org.apache.http.client.methods.HttpPost;
import org.apache.http.concurrent.Cancellable;

import raven.abstractions.closure.Action1;
import raven.abstractions.data.BulkInsertChangeNotification;
import raven.abstractions.data.BulkInsertOptions;
import raven.abstractions.data.Constants;
import raven.abstractions.data.DocumentChangeTypes;
import raven.abstractions.data.HttpMethods;
import raven.abstractions.json.linq.RavenJObject;
import raven.abstractions.json.linq.RavenJToken;
import raven.client.changes.IDatabaseChanges;
import raven.client.connection.ServerClient;
import raven.client.connection.implementation.HttpJsonRequest;
import de.undercouch.bson4jackson.BsonFactory;
import de.undercouch.bson4jackson.BsonGenerator;


public class RemoteBulkInsertOperation implements ILowLevelBulkInsertOperation {

  private final BsonFactory bsonFactory = new BsonFactory();

  private final static RavenJObject END_OF_QUEUE_OBJECT = RavenJObject.parse("{ \"QueueFinished\" : true }");

  //TODO: private CancellationTokenSource cancellationTokenSource;
  private final ServerClient operationClient;

  private final IDatabaseChanges operationChanges;
  private final ByteArrayOutputStream bufferedStream = new ByteArrayOutputStream();
  private final BlockingQueue<RavenJObject> queue;

  private HttpJsonRequest operationRequest;
  private byte[] responseBytes;
  private final Thread operationTask;
  private int total;

  private Action1<String> report;
  private UUID operationId;
  private transient boolean disposed;

  @Override
  public UUID getOperationId() {
    return operationId;
  }

  @Override
  public Action1<String> getReport() {
    return report;
  }

  @Override
  public void setReport(Action1<String> report) {
    this.report = report;
  }



  public RemoteBulkInsertOperation(BulkInsertOptions options, ServerClient client, IDatabaseChanges changes) {
    //TODO: synchronizationContext (???)
    operationId = UUID.randomUUID();
    operationClient = client;
    operationChanges = changes;
    queue = new ArrayBlockingQueue<>(Math.max(128, (options.getBatchSize() * 3) / 2));

    operationTask = startBulkInsertAsync(options);
    subscribeToBulkInsertNotifications(changes);
  }

  private void subscribeToBulkInsertNotifications(IDatabaseChanges changes) {
    // TODO Auto-generated method stub
  }

  private class BulkInsertEntity implements HttpEntity {

    private BulkInsertOptions options;

    public BulkInsertEntity(BulkInsertOptions options) {
      this.options = options;
    }

    @Override
    public boolean isRepeatable() {
      return false;
    }

    @Override
    public boolean isChunked() {
      return true;
    }

    @Override
    public long getContentLength() {
      return 0;
    }

    @Override
    public Header getContentType() {
      return null;
    }

    @Override
    public Header getContentEncoding() {
      return null;
    }

    @Override
    public InputStream getContent() throws IOException, IllegalStateException {
      throw new IllegalStateException("Not supported!");
    }

    @Override
    public void writeTo(OutputStream outstream) throws IOException {
      writeQueueToServer(outstream, options, null); //TODO: cancelation token
    }

    @Override
    public boolean isStreaming() {
      return true;
    }

    @Deprecated
    @Override
    public void consumeContent() throws IOException {
      //empty
    }

  }

  private Thread startBulkInsertAsync(BulkInsertOptions options) {
    operationClient.setExpect100Continue(true);

    String operationUrl = createOperationUrl(options);
    String token = getToken(operationUrl);
    try {
      token = validateThatWeCanUseAuthenticateTokens(operationUrl, token);
    } catch (Exception e) {
      throw new IllegalStateException("Could not authenticate token for bulk insert, if you are using ravendb in IIS make sure you have Anonymous Authentication enabled in the IIS configuration", e);
    }

    operationRequest = createOperationRequest(operationUrl, token);
    HttpPost webRequest = (HttpPost) operationRequest.getWebRequest();
    webRequest.setEntity(new BulkInsertEntity(options)); //TODO: cancelation task


    Thread thread = new Thread(new Runnable() {

      @Override
      public void run() {
        try {
          responseBytes = operationRequest.readResponseBytes();
        } catch (IOException e) {
          // TODO Auto-generated catch block
          e.printStackTrace();
          throw new RuntimeException(e);
        }
      }
    });

    operationClient.setExpect100Continue(false);

    thread.start();
    return thread;

    /* TODO:
    var cancellationToken = CreateCancellationToken();
    await Task.Factory.StartNew(() => WriteQueueToServer(stream, options, cancellationToken), TaskCreationOptions.LongRunning);
     */
  }

  /* TODO
   * private CancellationToken CreateCancellationToken()
        {
            cancellationTokenSource = new CancellationTokenSource();
            return cancellationTokenSource.Token;
        }
   */

  private String getToken(String operationUrl) {
    RavenJToken jsonToken = getAuthToken(operationUrl);
    return jsonToken.value(String.class, "Token");
  }

  private RavenJToken getAuthToken(String operationUrl) {
    //TODO: check if resource are disposed
    HttpJsonRequest request = operationClient.createRequest(HttpMethods.POST, operationUrl + "&op=generate-single-use-auth-token", true);
    return request.readResponseJson();
  }

  private String validateThatWeCanUseAuthenticateTokens(String operationUrl, String token) {
    HttpJsonRequest request = operationClient.createRequest(HttpMethods.POST, operationUrl + "&op=generate-single-use-auth-token", true);
    request.addOperationHeader("Single-Use-Auth-Token", token);
    RavenJToken result = request.readResponseJson();
    return result.value(String.class, "Token");
  }

  private HttpJsonRequest createOperationRequest(String operationUrl, String token) {
    HttpJsonRequest request = operationClient.createRequest(HttpMethods.POST, operationUrl, true);

    request.prepareForLongRequest();
    request.addOperationHeader("Single-Use-Auth-Token", token);

    return request;
  }

  private String createOperationUrl(BulkInsertOptions options) {
    String requestUrl = "/bulkInsert?";
    if (options.isCheckForUpdates()) {
      requestUrl += "checkForUpdates=true";
    }
    if (options.isCheckReferencesInIndexes()) {
      requestUrl += "&checkReferencesInIndexes=true";
    }

    requestUrl += "&operationId=" + operationId;

    return requestUrl;
  }
  //TODO: can we use cancellable from org.apache.http.concurrent; ?
  private void writeQueueToServer(OutputStream stream, BulkInsertOptions options, Cancellable cancellationToken) throws IOException {

    while (true) {
      //TODO: cancellationToken.ThrowIfCancellationRequested();
      List<RavenJObject> batch = new ArrayList<>();
      try {
        RavenJObject document;
        while ((document = queue.poll(200, TimeUnit.MICROSECONDS)) != null) {
          //TODO: cancellationToken.ThrowIfCancellationRequested();

          if (document == END_OF_QUEUE_OBJECT) { //marker
            flushBatch(stream, batch);
            return;
          }
          batch.add(document);

          if (batch.size() >= options.getBatchSize()) {
            break;
          }
        }
      } catch (InterruptedException e ){
        //ignore
      }
      flushBatch(stream, batch);
    }
  }

  @Override
  public void write(String id, RavenJObject metadata, RavenJObject data) {
    if (id == null) {
      throw new IllegalArgumentException("id");
    }
    if (metadata == null) {
      throw new IllegalArgumentException("metadata");
    }
    if (data == null) {
      throw new IllegalArgumentException("data");
    }

    /*
    if (operationTask.isCancelled()) { //TODOO or isFaulted
      operationTask.get();// error early if we have  any error
    }*/
    metadata.add("@id", id);
    data.add(Constants.METADATA, metadata);
    try {
      while (!queue.offer(data)) {
        Thread.sleep(250);
      }
    } catch (InterruptedException e) {
      throw new RuntimeException(e);
    }
  }


  private boolean isOperationCompleted(long operationId) {
    RavenJToken status = getOperationStatus(operationId);
    if (status == null) {
      return true;
    }
    if (status.value(Boolean.class, "Completed")) {
      return true;
    }
    return false;
  }

  private RavenJToken getOperationStatus(long operationId) {
    return operationClient.getOperationStatus(operationId);
  }


  @Override
  public void close() throws InterruptedException {
    if (disposed) {
      return ;
    }
    queue.add(END_OF_QUEUE_OBJECT);
    operationTask.join();

    //TODO:  operationTask.AssertNotFailed();

    reportInternal("Finished writing all results to server");

    long operationId = 0;

    RavenJObject result = RavenJObject.parse(new String(responseBytes));
    operationId = result.value(Long.class, "OperationId");

    while (true) {
      if (isOperationCompleted(operationId)) {
        break;
      }
      Thread.sleep(500);
    }
    reportInternal("Done writing to server");
  }

  private  void flushBatch(OutputStream requestStream, Collection<RavenJObject> localBatch) throws IOException {
    if (localBatch.isEmpty()) {
      return ;
    }
    bufferedStream.reset();
    writeToBuffer(localBatch);

    byte[] bytes = ByteBuffer.allocate(4).putInt(bufferedStream.size()).array();
    ArrayUtils.reverse(bytes);
    requestStream.write(bytes);
    bufferedStream.writeTo(requestStream);
    requestStream.flush();

    total += localBatch.size();

    Action1<String> report = getReport();
    if (report != null) {
      report.apply(String.format("Wrote %d (total %d) documents to server gzipped to %d kb", localBatch.size(), total, bufferedStream.size() / 1024));
    }

  }

  private void writeToBuffer(Collection<RavenJObject> localBatch) throws IOException {
    GZIPOutputStream gzipOutputStream = new GZIPOutputStream(bufferedStream);

    BsonGenerator bsonWriter = bsonFactory.createJsonGenerator(gzipOutputStream);
    bsonWriter.disable(org.codehaus.jackson.JsonGenerator.Feature.AUTO_CLOSE_TARGET);

    byte[] bytes = ByteBuffer.allocate(4).putInt(localBatch.size()).array();
    ArrayUtils.reverse(bytes);
    gzipOutputStream.write(bytes);
    for (RavenJObject doc : localBatch) {
      doc.writeTo(bsonWriter);
    }
    bsonWriter.close();
    gzipOutputStream.finish();
    bufferedStream.flush();
  }

  private void reportInternal(String format, Object... args) {
    Action1<String> onReport = report;
    if (onReport != null) {
      onReport.apply(String.format(format, args));
    }
  }

  public void onNExt(BulkInsertChangeNotification value) {
    if (value.getType().equals(DocumentChangeTypes.BULK_INSERT_ERROR)) {
      //TODO:  cancellationTokenSource.Cancel();
    }
  }
}
