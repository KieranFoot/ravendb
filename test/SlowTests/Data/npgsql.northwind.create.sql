DROP SCHEMA public;
CREATE SCHEMA public;
ALTER SCHEMA public OWNER TO postgres;

/*==============================================================*/
/* Table: NoPkTable                                             */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS NoPkTable (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY
);



/*==============================================================*/
/* Table: UnsupportedTable                                      */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS UnsupportedTable (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY, 
   Node                 point          not null,
   constraint PK_UnsupportedTable primary key (Id) 
);


/*==============================================================*/
/* Table: Customer                                              */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS Customer (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY,
   FirstName            CHARACTER VARYING(40)         not null,
   Pic			        bytea	     ,
   constraint PK_CUSTOMER primary key (Id)
);


/*==============================================================*/
/* Table: Order                                                 */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS "Order" (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY,
   OrderDate            timestamp without time zone DEFAULT now() NOT NULL,
   CustomerId          integer                 not null,
   TotalAmount          NUMERIC(12,2)         default 0,
   constraint PK_ORDER primary key (Id)
);


/*==============================================================*/
/* Table: OrderItem                                             */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS OrderItem (  
   OrderId             integer                 GENERATED BY DEFAULT AS IDENTITY,
   ProductId           integer                 not null,
   UnitPrice            NUMERIC(12,2)        not null default 0,
   constraint PK_ORDERITEM primary key (OrderID, ProductID)
);



/*==============================================================*/
/* Table: Details                                               */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS Details (  
   ID                  integer                 GENERATED BY DEFAULT AS IDENTITY,
   OrderId             integer                 not null,
   ProductId           integer                 not null,
   Name			        CHARACTER VARYING(30)	     ,
   constraint PK_DETAILS primary key (ID, OrderID, ProductID)
);


/*==============================================================*/
/* Table: Product                                               */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS Product (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY,
   UnitPrice            NUMERIC(12,2)         default 0,
   IsDiscontinued       BOOLEAN                  not null default false,
   constraint PK_PRODUCT primary key (Id)
);

/*==============================================================*/
/* Table: Category                                              */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS Category (
   Id                  integer                 GENERATED BY DEFAULT AS IDENTITY,
   Name			        CHARACTER VARYING(30)	     not null,
   constraint PK_CATEGORY primary key (Id)
);


/*==============================================================*/
/* Table: ProductCategory                                       */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS ProductCategory (
   ProductId             integer                 not null,
   CategoryId            integer                 not null,
   constraint PK_PRODUCT_CATEGORY primary key (ProductId, CategoryId)
);


/*==============================================================*/
/* Table: Photo                                                 */
/*==============================================================*/
CREATE TABLE IF NOT EXISTS Photo (  
   Id			int		             GENERATED BY DEFAULT AS IDENTITY,
   Pic			bytea	     ,	   
   Photographer	int		             ,
   InPic1		int                  ,
   InPic2		int		             ,
   constraint PK_Photo primary key (Id)
);


alter table "Order"
   add constraint FK_ORDER_REFERENCE_CUSTOMER foreign key (CustomerId)
      references Customer (Id);


alter table OrderItem
   add constraint FK_ORDERITE_REFERENCE_ORDER foreign key (OrderId)
      references "Order" (Id);


alter table Details
   add constraint FK_Details_REFERENCE_ORDERITEM foreign key (OrderId, ProductId)
      references OrderItem(OrderId, ProductId);

alter table OrderItem
   add constraint FK_ORDERITE_REFERENCE_PRODUCT foreign key (ProductId)
      references Product (Id);


alter table Photo
   add constraint FK_ORDERITE_REFERENCE_CUSTOMER1 foreign key (Photographer)
      references Customer (Id);


alter table Photo
   add constraint FK_ORDERITE_REFERENCE_CUSTOMER2 foreign key (InPic1)
      references Customer (Id);


alter table Photo
   add constraint FK_ORDERITE_REFERENCE_CUSTOMER3 foreign key (InPic2)
      references Customer (Id);

ALTER TABLE ProductCategory
    ADD CONSTRAINT FK_PROD_CAT_REF_PROD FOREIGN KEY (ProductId)
      REFERENCES Product (Id);
      
ALTER TABLE ProductCategory
    ADD CONSTRAINT FK_PROD_CAT_REF_CAT FOREIGN KEY (CategoryId)
        REFERENCES Category(Id);


CREATE VIEW john_customers
AS SELECT * FROM Customer
WHERE FirstName = 'John';
