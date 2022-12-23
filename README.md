# Agirax Hosting Server

Agiras is an HTTP server that uses Sisk to host static files, Sisk modules and PHP applications. At the moment its development is still in the alpha phase, and therefore, **it should not be used for production**.

Known issues:

- It is still not allowed to run PHP applications that do not redirect inputs to the index.php file (like Wordpress websites).
- Agirax needs Nginx in order to run PHP applications over FastCGI, as there is still no implementation of FastCGI running in Agirax.
- It may still be unsafe to host static files as not enough testing has been done and there is no content-type filter.
- Sisk modules require their dependencies to be exported along with the libraries.
- It has not been tested on platforms other than Windows.